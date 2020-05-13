namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Net.Sockets
open System.Net
open System.Diagnostics
open System.IO

open NBitcoin
open DotNetLightning.Utils
open DotNetLightning.Serialize
open DotNetLightning.Serialize.Msgs
open DotNetLightning.Channel
open DotNetLightning.Peer
open DotNetLightning.Chain
open DotNetLightning.Transactions

open GWallet.Backend
open GWallet.Backend.UtxoCoin
open GWallet.Backend.FSharpUtil.UwpHacks

open FSharp.Core


module Lightning =
    let private hex = DataEncoders.HexEncoder()

    let GetIndexOfDestinationInOutputSeq (dest: IDestination) (outputs: seq<IndexedTxOut>): TxOutIndex =
        let hasRightDestination (output: IndexedTxOut): bool =
            output.TxOut.IsTo dest
        let matchingOutput: IndexedTxOut =
            Seq.find hasRightDestination outputs
        TxOutIndex <| uint16 matchingOutput.N

    let private featureBits =
        let featureBits = FeatureBit.Zero
        featureBits.SetFeature Feature.OptionDataLossProtect FeaturesSupport.Optional true
        featureBits

    let plainInit: Init = {
        Features = featureBits
        TLVStream = [||]
    }

    let private bolt08EncryptedMessageLengthPrefixLength = 18
    // https://github.com/lightningnetwork/lightning-rfc/blob/master/08-transport.md#authenticated-key-exchange-handshake-specification
    let private bolt08ActOneLength = 50
    let private bolt08ActTwoLength = 50
    let private bolt08ActThreeLength = 66

    type StringErrorInner =
        {
            Msg: string
            During: string
        }

    type LNError =
    | DNLError of ErrorMessage
    | StringError of StringErrorInner
    | DNLChannelError of ChannelError
    | PeerDisconnected of bool

    let ReadExactAsync (stream: NetworkStream)
                       (numberBytesToRead: int)
                           : Async<Result<array<byte>, LNError>> =
        let buf: array<byte> = Array.zeroCreate numberBytesToRead
        let rec read buf totalBytesRead = async {
            let! bytesRead =
                stream.ReadAsync(buf, totalBytesRead, (numberBytesToRead - totalBytesRead))
                |> Async.AwaitTask
            let totalBytesRead = totalBytesRead + bytesRead
            if bytesRead = 0 then
                if totalBytesRead = 0 then
                    return Error (PeerDisconnected false)
                else
                    return Error (PeerDisconnected true)
            else
                if totalBytesRead < numberBytesToRead then
                    return! read buf totalBytesRead
                else
                    return Ok buf
        }
        read buf 0

    let ReadAsync (keyRepo: DefaultKeyRepository) (peer: Peer) (stream: NetworkStream): Async<Result<PeerCommand, LNError>> =
        match peer.ChannelEncryptor.GetNoiseStep() with
        | ActTwo ->
            async {
                let! actTwoRes = ReadExactAsync stream bolt08ActTwoLength
                match actTwoRes with
                | Ok actTwo ->
                    return Ok (ProcessActTwo(actTwo, keyRepo.NodeSecret.PrivateKey))
                | Error err ->
                    return Error err
            }
        | ActThree ->
            async {
                let! actThreeRes = ReadExactAsync stream bolt08ActThreeLength
                match actThreeRes with
                | Ok actThree ->
                    return Ok (ProcessActThree actThree)
                | Error err ->
                    return Error err
            }
        | NoiseComplete ->
            async {
                let! encryptedLengthRes = ReadExactAsync stream bolt08EncryptedMessageLengthPrefixLength
                match encryptedLengthRes with
                | Ok encryptedLength ->
                    let reader length =
                        let buf = Array.zeroCreate length
                        Debug.Assert ((stream.Read (buf, 0, length)) = length, "read length not equal to requested length")
                        buf
                    return Ok (DecodeCipherPacket (encryptedLength, reader))
                | Error err ->
                    return Error err
            }
        | _ ->
            failwith "unreachable"

    type MessageReceived =
    | ChannelMessage of Peer * IChannelMsg
    | OurErrorMessage of Peer * ErrorMessage
    | OtherMessage of Peer

    let ProcessPeerEvents (oldPeer: Peer) (peerEventsResult: Result<List<PeerEvent>,'a>): MessageReceived =
        match peerEventsResult with
        | Ok (evt::[]) ->
            match evt with
            | PeerEvent.ReceivedChannelMsg (chanMsg, _) ->
                ChannelMessage (Peer.applyEvent oldPeer evt, chanMsg)
            | PeerEvent.ReceivedError (error, _) ->
                OurErrorMessage (Peer.applyEvent oldPeer evt, error)
            | _ ->
                Infrastructure.LogDebug <| SPrintF1 "Warning: ignoring event that was not ReceivedChannelMsg, it was: %s"
                                                    (evt.GetType().Name)
                OtherMessage <| Peer.applyEvent oldPeer evt
        | Ok _ ->
            failwith "receiving more than one channel event"
        | Error peerError ->
            failwith <| SPrintF1 "couldn't parse chan msg: %s" (peerError.ToString())

    let peerLimits: ChannelHandshakeLimits = {
        ForceChannelAnnouncementPreference = false
        MinFundingSatoshis = Money 100L
        MaxHTLCMinimumMSat = LNMoney 100000L
        MinMaxHTLCValueInFlightMSat = LNMoney 10000L
        MaxChannelReserveSatoshis = Money 100000L
        MinMaxAcceptedHTLCs = 1us
        MinDustLimitSatoshis = Money 100L
        MaxDustLimitSatoshis = Money 10000000L
        MaxMinimumDepth = BlockHeightOffset32 UInt32.MaxValue // TODO make optional in DotNetLightning
        MaxClosingNegotiationIterations = 10
    }

    // Only used when accepting channels.
    // It is 'high', since low values are not accepted by alternate implementations.
    // TODO: test lower values
    let handshakeConfig = { ChannelHandshakeConfig.MinimumDepth = BlockHeightOffset32 6u }

    // Used for e.g. option_upfront_shutdown_script in
    // https://github.com/lightningnetwork/lightning-rfc/blob/master/02-peer-protocol.md#rationale-4
    let CreatePayoutScript (account: IAccount) =
        let scriptAddress = BitcoinScriptAddress (account.PublicAddress, Config.BitcoinNet)
        scriptAddress.ScriptPubKey

    let CreateChannelOptions (account: IAccount): ChannelOptions = {
        AnnounceChannel = false
        FeeProportionalMillionths = 100u
        MaxFeeRateMismatchRatio = 1.
        ShutdownScriptPubKey = Some (CreatePayoutScript account)
    }

    let CreateChannelConfig (account: IAccount): ChannelConfig = {
        ChannelHandshakeConfig = handshakeConfig
        PeerChannelConfigLimits = peerLimits
        ChannelOptions = CreateChannelOptions account
    }

    let CreateChannel (account: IAccount) =
        let channelConfig = CreateChannelConfig account
        Channel.CreateCurried channelConfig

    type ChannelEnvironment = { Account: UtxoCoin.NormalUtxoAccount; NodeIdForResponder: NodeId; KeyRepo: DefaultKeyRepository }
    type Connection = { Init: Init; Peer: Peer; Client: TcpClient }

    let Send (msg: ILightningMsg) (peer: Peer) (stream: NetworkStream): Async<Peer> =
        async {
            let plaintext = msg.ToBytes()
            let ciphertext, newPeerChannelEncryptor = PeerChannelEncryptor.encryptMessage plaintext peer.ChannelEncryptor
            let sentPeer = { peer with ChannelEncryptor = newPeerChannelEncryptor }
            do! stream.WriteAsync(ciphertext, 0, ciphertext.Length) |> Async.AwaitTask
            return sentPeer
        }

    let ReportDisconnection (ipEndPoint: IPEndPoint)
                            (nodeId: NodeId)
                            (abruptly: bool)
                            (context: string) =
        let msg =
            SPrintF5
                "peer %s@%s:%i disconnected %s(context: %s)"
                (nodeId.Value.ToString())
                (ipEndPoint.Address.ToString())
                ipEndPoint.Port
                (if abruptly then "after sending partial message " else "")
                context
        Infrastructure.ReportWarningMessage msg

    let ConnectAndHandshake ({ Account = _; NodeIdForResponder = nodeIdForResponder; KeyRepo = keyRepo }: ChannelEnvironment)
                            (channelCounterpartyIP: IPEndPoint)
                                : Async<Result<Connection, LNError>> =
        async {
            let responderId = channelCounterpartyIP :> EndPoint |> PeerId
            let initialPeer = Peer.CreateOutbound(responderId, nodeIdForResponder, keyRepo.NodeSecret.PrivateKey)
            let act1, peerEncryptor = PeerChannelEncryptor.getActOne initialPeer.ChannelEncryptor
            Debug.Assert((bolt08ActOneLength = act1.Length), "act1 has wrong length")
            let sentAct1Peer = { initialPeer with ChannelEncryptor = peerEncryptor }

            let client = new TcpClient (channelCounterpartyIP.AddressFamily)
            Infrastructure.LogDebug <| SPrintF1 "Connecting over TCP to %A..." channelCounterpartyIP
            do! client.ConnectAsync(channelCounterpartyIP.Address, channelCounterpartyIP.Port) |> Async.AwaitTask
            let stream = client.GetStream()
            do! stream.WriteAsync(act1, 0, act1.Length) |> Async.AwaitTask

            // Receive act2
            Infrastructure.LogDebug "Receiving Act 2..."
            let! act2Res = ReadAsync keyRepo sentAct1Peer stream
            match act2Res with
            | Error (PeerDisconnected abruptly) ->
                ReportDisconnection channelCounterpartyIP nodeIdForResponder abruptly "receiving act 2"
                return Error (PeerDisconnected abruptly)
            | Error err -> return Error err
            | Ok act2 ->
                let actThree, receivedAct2Peer =
                    match Peer.executeCommand sentAct1Peer act2 with
                    | Ok (ActTwoProcessed ((actThree, _nodeId), _) as evt::[]) ->
                        let peer = Peer.applyEvent sentAct1Peer evt
                        actThree, peer
                    | Ok _ ->
                        failwith "not one good ActTwoProcessed event"
                    | Error peerError ->
                        failwith <| SPrintF1 "couldn't parse act2: %s" (peerError.ToString())

                Debug.Assert((bolt08ActThreeLength = actThree.Length), SPrintF1 "act3 has wrong length (not %i)" bolt08ActThreeLength)
                do! stream.WriteAsync(actThree, 0, actThree.Length) |> Async.AwaitTask

                let! sentInitPeer = Send plainInit receivedAct2Peer stream

                // receive init
                Infrastructure.LogDebug "Receiving init..."
                let! initRes = ReadAsync keyRepo sentInitPeer stream
                match initRes with
                | Error (PeerDisconnected abruptly) ->
                    let context = SPrintF1 "receiving init message while connecting, our init: %A" plainInit
                    ReportDisconnection channelCounterpartyIP nodeIdForResponder abruptly context
                    return Error (PeerDisconnected abruptly)
                | Error err -> return Error err
                | Ok init ->
                    return
                        match Peer.executeCommand sentInitPeer init with
                        | Ok (ReceivedInit (newInit, _) as evt::[]) ->
                            let peer = Peer.applyEvent sentInitPeer evt
                            Ok { Init = newInit; Peer = peer; Client = client }
                        | Ok _ ->
                            failwith "not one good ReceivedInit event"
                        | Error peerError ->
                            failwith <| SPrintF1 "couldn't parse init: %s" (peerError.ToString())
        }

    let rec ReadUntilChannelMessage (keyRepo: DefaultKeyRepository, peer: Peer, stream: NetworkStream): Async<Result<Peer * IChannelMsg, LNError>> =
        async {
            let! channelMsgRes = ReadAsync keyRepo peer stream
            match channelMsgRes with
            | Error err -> return Error err
            | Ok channelMsg ->
                let messageReceived =
                    channelMsg
                    |> Peer.executeCommand peer
                    |> ProcessPeerEvents peer
                match messageReceived with
                | ChannelMessage (newPeer, chanMsg) ->
                    return Ok (newPeer, chanMsg)
                | OurErrorMessage (_, dnlErrorMessage) ->
                    return Error <| DNLError dnlErrorMessage
                | OtherMessage newPeer ->
                    return! ReadUntilChannelMessage (keyRepo, newPeer, stream)
        }

    let GetLocalParams (isFunder: Boolean)
                       (fundingAmount: Money)
                       (nodeIdForResponder: NodeId)
                       (account: NormalUtxoAccount)
                       (keyRepo: DefaultKeyRepository)
                           : ChannelKeys * LocalParams =
        let channelKeys: ChannelKeys = (keyRepo :> IKeysRepository).GetChannelKeys false
        let channelPubkeys: ChannelPubKeys = channelKeys.ToChannelPubKeys()
        channelKeys, {
            Features = featureBits
            NodeId = nodeIdForResponder
            ChannelPubKeys = channelPubkeys
            DustLimitSatoshis = Money 5UL
            MaxHTLCValueInFlightMSat = LNMoney 5000L
            // BOLT #2 recommends a channel reserve of 1% of the channel capacity
            ChannelReserveSatoshis = fundingAmount / 100L
            HTLCMinimumMSat = LNMoney 1000L
            ToSelfDelay = BlockHeightOffset16 6us
            MaxAcceptedHTLCs = uint16 10
            IsFunder = isFunder
            DefaultFinalScriptPubKey = account |> CreatePayoutScript
        }

    let GetAcceptChannel ({ Account = account; NodeIdForResponder = nodeIdForResponder; KeyRepo = keyRepo }: ChannelEnvironment)
                         ({ Init = receivedInit; Peer = receivedInitPeer; Client = client }: Connection)
                         (channelCapacity: TransferAmount)
                         (metadata: TransactionMetadata)
                         (password: unit -> string)
                         (balance: decimal)
                         (temporaryChannelId: ChannelId)
                             : Async<Result<AcceptChannel * Channel * Peer, LNError>> =
        let fundingTxProvider (dest: IDestination, amount: Money, _: FeeRatePerKw) =
            let transferAmount = TransferAmount (amount.ToDecimal MoneyUnit.BTC, balance, Currency.BTC)
            Debug.Assert (
                             (transferAmount.ValueToSend = channelCapacity.ValueToSend),
                             SPrintF2 "amount passed to fundingTxProvider %A not equal channelCapacity %A"
                                      transferAmount.ValueToSend
                                      channelCapacity.ValueToSend
                         )
            let transactionHex = UtxoCoin.Account.SignTransactionForDestination account metadata dest transferAmount (password ())
            let fundingTransaction = Transaction.Load (hex.DecodeData transactionHex, Config.BitcoinNet)
            let outputs = fundingTransaction.Outputs.AsIndexedOutputs ()
            (fundingTransaction |> FinalizedTx, GetIndexOfDestinationInOutputSeq dest outputs) |> Ok

        let fundingAmount = Money (channelCapacity.ValueToSend, MoneyUnit.BTC)
        let channelKeys, localParams = GetLocalParams true fundingAmount nodeIdForResponder account keyRepo

        async {
            let! feeEstimator = FeeEstimator.Create ()
            let feeEstimator = feeEstimator :> IFeeEstimator
            let initFunder =
                {
                    InputInitFunder.PushMSat = LNMoney.MilliSatoshis 0L
                    TemporaryChannelId = temporaryChannelId
                    FundingSatoshis = fundingAmount
                    InitFeeRatePerKw = feeEstimator.GetEstSatPer1000Weight <| ConfirmationTarget.Normal
                    FundingTxFeeRatePerKw = feeEstimator.GetEstSatPer1000Weight <| ConfirmationTarget.Normal
                    LocalParams = localParams
                    RemoteInit = receivedInit
                    ChannelFlags = 0uy
                    ChannelKeys = channelKeys
                }

            let chanCmd = ChannelCommand.CreateOutbound initFunder
            let stream = client.GetStream()
            let channelCounterpartyIP = client.Client.RemoteEndPoint :?> IPEndPoint
            let initialChan: Channel = CreateChannel
                                           account
                                           keyRepo
                                           feeEstimator
                                           keyRepo.NodeSecret.PrivateKey
                                           fundingTxProvider
                                           Config.BitcoinNet
                                           nodeIdForResponder

            let openChanMsg, sentOpenChan =
                match Channel.executeCommand initialChan chanCmd with
                | Ok (NewOutboundChannelStarted (openChanMsg, _) as evt::[]) ->
                    let chan = Channel.applyEvent initialChan evt
                    openChanMsg, chan
                | Ok evtList ->
                    failwith <| SPrintF1 "event was not a single NewOutboundChannelStarted, it was: %A" evtList
                | Error channelError ->
                    failwith <| SPrintF1 "could not execute channel command: %s" (channelError.ToString())
            let! sentOpenChanPeer = Send openChanMsg receivedInitPeer stream

            // receive acceptchannel
            Infrastructure.LogDebug "Receiving accept_channel..."
            let! msgRes = ReadUntilChannelMessage (keyRepo, sentOpenChanPeer, stream)
            match msgRes with
            | Error (PeerDisconnected abruptly) ->
                let context = SPrintF1 "receiving accept_channel, our open_channel == %A" openChanMsg
                ReportDisconnection channelCounterpartyIP nodeIdForResponder abruptly context
                return Error (PeerDisconnected abruptly)
            | Error errorMsg -> return Error errorMsg
            | Ok (receivedOpenChanReplyPeer, chanMsg) ->
                match chanMsg with
                | :? AcceptChannel as acceptChannel ->
                    return Ok (acceptChannel, sentOpenChan, receivedOpenChanReplyPeer)
                | _ ->
                    return Error <| StringError {
                        Msg = SPrintF1 "channel message is not accept channel: %s" (chanMsg.GetType().Name)
                        During = "waiting for accept_channel"
                    }
        }

    let ContinueFromAcceptChannel (keyRepo: DefaultKeyRepository)
                                  (acceptChannel: AcceptChannel)
                                  (sentOpenChan: Channel)
                                  (stream: NetworkStream)
                                  (receivedOpenChanReplyPeer: Peer)
                                      : Async<Result<string * Channel, LNError>> =
        async {
            match Channel.executeCommand sentOpenChan (ApplyAcceptChannel acceptChannel) with
            | Ok (ChannelEvent.WeAcceptedAcceptChannel(fundingCreated, _) as evt::[]) ->
                let channelCounterpartyIP = receivedOpenChanReplyPeer.PeerId.Value :?> IPEndPoint
                let nodeIdForResponder = receivedOpenChanReplyPeer.TheirNodeId.Value
                let receivedAcceptChannelChan = Channel.applyEvent sentOpenChan evt

                let! sentFundingCreatedPeer = Send fundingCreated receivedOpenChanReplyPeer stream

                Infrastructure.LogDebug "Receiving funding_created..."
                let! msgRes = ReadUntilChannelMessage (keyRepo, sentFundingCreatedPeer, stream)
                match msgRes with
                | Error (PeerDisconnected abruptly) ->
                    let context = SPrintF1 "receiving funding_created, their accept_channel == %A" acceptChannel
                    ReportDisconnection channelCounterpartyIP nodeIdForResponder abruptly context
                    return Error (PeerDisconnected abruptly)
                | Error errorMsg ->
                    return Error errorMsg
                | Ok (_, chanMsg) ->
                    match chanMsg with
                    | :? FundingSigned as fundingSigned ->
                        let chanCmd = ChannelCommand.ApplyFundingSigned fundingSigned
                        let chanEvents = Channel.executeCommand receivedAcceptChannelChan chanCmd
                        match chanEvents with
                        | Ok (ChannelEvent.WeAcceptedFundingSigned (finalizedTx, _) as evt::[]) ->
                            let chan = Channel.applyEvent receivedAcceptChannelChan evt
                            let signedTx: string = finalizedTx.Value.ToHex()
                            let! txId = Account.BroadcastRawTransaction Currency.BTC signedTx
                            return Ok (txId, chan)
                        | Ok evt ->
                            let msg = SPrintF1 "not one good WeAcceptedFundingSigned chan evt: %s" (evt.GetType().Name)
                            let innerError = { StringErrorInner.Msg = msg; During = "applying their funding_signed message" }
                            return Error <| StringError innerError
                        | Error e ->
                            let msg = SPrintF1 "bad result when expecting WeAcceptedFundingSigned: %s" (e.ToString())
                            let innerError = { Msg = msg; During = "applying their funding_signed message" }
                            return Error <| StringError innerError
                    | _ ->
                        return Error <| StringError {
                            Msg = SPrintF1 "channel message is not funding signed: %s" (chanMsg.GetType().Name)
                            During = "waiting for funding_signed message from counterparty"
                        }

            | Ok evtList ->
                let msg = SPrintF1 "event was not a single WeAcceptedAcceptChannel, it was: %A" evtList
                let innerError = { Msg = msg; During = "applying their accept_channel message" }
                return Error <| StringError innerError
            | Error channelError ->
                match channelError with
                | InvalidAcceptChannel invalidAcceptChannelError ->
                    let msg = SPrintF1 "DNL detected an invalid accept_channel message from fundee: %A"
                                        invalidAcceptChannelError.Errors
                    let innerError = { Msg = msg; During = "applying their accept_channel_message" }
                    return Error <| StringError innerError
                | _ ->
                    let msg = SPrintF1 "unrecognized error from DNL: %A" channelError
                    let innerError = { Msg = msg; During = "applying their accept_channel_message" }
                    return Error <| StringError innerError
            }

    let GetSeedAndRepo (random: Random): uint256 * DefaultKeyRepository * ChannelId =
        let channelKeysSeedBytes = Array.zeroCreate 32
        random.NextBytes channelKeysSeedBytes
        let channelKeysSeed = uint256 channelKeysSeedBytes
        let keyRepo = SerializedChannel.UIntToKeyRepo channelKeysSeed
        let temporaryChannelIdBytes: array<byte> = Array.zeroCreate 32
        random.NextBytes temporaryChannelIdBytes
        let temporaryChannelId =
            temporaryChannelIdBytes
            |> uint256
            |> ChannelId
        channelKeysSeed, keyRepo, temporaryChannelId

    let GetNewChannelFilename(): string =
        SerializedChannel.ChannelFilePrefix
            // this offset is the approximate time this feature was added (making filenames shorter)
            + (DateTimeOffset.Now.ToUnixTimeSeconds() - 1574212362L |> string)
            + SerializedChannel.ChannelFileEnding

    let ContinueFromAcceptChannelAndSave (account: UtxoCoin.NormalUtxoAccount)
                                         (channelKeysSeed: uint256)
                                         (channelCounterpartyIP: IPEndPoint)
                                         (acceptChannel: AcceptChannel)
                                         (chan: Channel)
                                         (stream: NetworkStream)
                                         (peer: Peer)
                                             : Async<Result<string, LNError>> = // TxId of Funding Transaction is returned
        async {
            let keyRepo = SerializedChannel.UIntToKeyRepo channelKeysSeed
            let! res = ContinueFromAcceptChannel keyRepo acceptChannel chan stream peer
            match res with
            | Error errorMsg -> return Error errorMsg
            | Ok (fundingTxId, receivedFundingSignedChan) ->
                let fileName = GetNewChannelFilename()
                SerializedChannel.Save account receivedFundingSignedChan channelKeysSeed channelCounterpartyIP acceptChannel.MinimumDepth fileName
                Infrastructure.LogDebug <| SPrintF1 "Channel saved to %s" fileName
                return Ok fundingTxId
        }

    let QueryBTCFast (command: (Async<StratumClient> -> Async<'T>)): Async<'T> =
        Server.Query
            Currency.BTC
            (QuerySettings.Default ServerSelectionMode.Fast)
            command
            None

    let PositionInBlockFromScriptCoin (txId: ChannelId) (fundingSCoin: ScriptCoin): Async<TxIndexInBlock * BlockHeight> =
        async {
            let txIdHex: string = txId.Value.ToString()
            let fundingDestination: TxDestination = fundingSCoin.ScriptPubKey.GetDestination ()
            let fundingAddress: BitcoinAddress = fundingDestination.GetAddress Config.BitcoinNet
            let fundingAddressString: string = fundingAddress.ToString ()
            let scriptHash = Account.GetElectrumScriptHashFromPublicAddress Currency.BTC fundingAddressString
            let! historyList =
                QueryBTCFast
                    (ElectrumClient.GetBlockchainScriptHashHistory scriptHash)
            let history = Seq.head historyList
            let fundingBlockHeight = BlockHeight history.Height
            let! merkleResult =
                QueryBTCFast
                    (ElectrumClient.GetBlockchainScriptHashMerkle txIdHex history.Height)
            return TxIndexInBlock (merkleResult: BlockchainScriptHashMerkleInnerResult).Pos, fundingBlockHeight
        }

    type NotReadyReason = {
        CurrentConfirmations: BlockHeightOffset32
        NeededConfirmations: BlockHeightOffset32
    }

    type ChannelMessageOrDeepEnough =
        | NotReady of NotReadyReason
        | DeepEnough of Async<ChannelCommand>

    type ChannelDepthAndAccount = {
        ChannelId: ChannelId
        ConfirmationsCount: BlockHeightOffset32
        Channel: Channel
        Account: UtxoCoin.NormalUtxoAccount
        SerializedChannel: SerializedChannel
        FundingScriptCoin: ScriptCoin
    }

    let JudgeDepth (details: ChannelDepthAndAccount)
                       : ChannelMessageOrDeepEnough =
        if details.ConfirmationsCount >= details.SerializedChannel.MinSafeDepth then
            DeepEnough <|
                async {
                    let! txIndex, fundingBlockHeight =
                        PositionInBlockFromScriptCoin
                            details.ChannelId
                            details.FundingScriptCoin
                    let channelCommand =
                        ChannelCommand.ApplyFundingConfirmedOnBC
                            (fundingBlockHeight, txIndex, details.ConfirmationsCount)
                    return channelCommand
                }
        else
            NotReady {
                CurrentConfirmations = details.ConfirmationsCount
                NeededConfirmations = details.SerializedChannel.MinSafeDepth
            }

    let GetFundingLockedMsg (channel: Channel) (channelCommand: ChannelCommand): Channel * FundingLocked =
        let channelEvents = Channel.executeCommand channel channelCommand
        match channelEvents with
        | Ok ((FundingConfirmed _ as evt1)::(WeSentFundingLocked fundingLockedMsg as evt2)::[]) ->
            let channelWithFundingConfirmed = Channel.applyEvent channel evt1
            let channelWithFundingLockedSent = Channel.applyEvent channelWithFundingConfirmed evt2
            channelWithFundingLockedSent, fundingLockedMsg
        | Ok events ->
            failwith <| SPrintF1 "not two good channel events: %A" (List.map (fun evt -> evt.GetType().Name) events)
        | Error e ->
            failwith <| SPrintF1 "bad result when expecting WeSentFundingLocked: %s" (e.ToString())

    let LoadChannelAndFetchDepth (fileName: string): Async<ChannelDepthAndAccount> =
        let serializedChannel = SerializedChannel.LoadSerializedChannel fileName
        let accountFileName = serializedChannel.AccountFileName

        let fromAccountFileToPublicAddress =
            UtxoCoin.Account.GetPublicAddressFromNormalAccountFile Currency.BTC

        let account =
            UtxoCoin.NormalUtxoAccount
                (
                    Currency.BTC,
                    {
                        Name = Path.GetFileName accountFileName
                        Content = fun _ -> File.ReadAllText accountFileName
                    },
                    fromAccountFileToPublicAddress,
                    UtxoCoin.Account.GetPublicKeyFromNormalAccountFile
                )

        async {
            let! feeEstimator = FeeEstimator.Create ()
            let feeEstimator = feeEstimator :> IFeeEstimator

            let channel =
                serializedChannel.ChannelFromSerialized
                    (CreateChannel account)
                    feeEstimator

            let (txId: ChannelId, fundingScriptCoin: ScriptCoin) =
                match channel.State with
                | ChannelState.WaitForFundingConfirmed waitForFundingConfirmedData ->
                    waitForFundingConfirmedData.ChannelId, waitForFundingConfirmedData.Commitments.FundingScriptCoin
                | ChannelState.Normal normalData ->
                    normalData.ChannelId, normalData.Commitments.FundingScriptCoin
                | _ as unknownState ->
                    // using failwith because this should never happen
                    failwith <| SPrintF1 "unexpected saved channel state: %s" (unknownState.GetType().Name)

            let txIdHex: string = txId.Value.ToString()
            let! confirmationsCount =
                async {
                    let! confirmations =
                        QueryBTCFast (ElectrumClient.GetConfirmations txIdHex)
                    return BlockHeightOffset32 confirmations
                }

            return {
                ChannelId = txId
                ConfirmationsCount = confirmationsCount
                Channel = channel
                Account = account
                SerializedChannel = serializedChannel
                FundingScriptCoin = fundingScriptCoin
            }
        }

    type ChannelStatus =
    // first element is always opening transaction id
    | UsableChannel of string
    | UnusableChannelWithReason of string * NotReadyReason

    let LoadChannelAndCheckChannelMessage (fileName: string): Async<Result<ChannelStatus, LNError>> =
        async {
            let! details = LoadChannelAndFetchDepth fileName
            let txIdHex = details.ChannelId.Value.ToString ()
            let notReestablishedChannel = details.Channel
            let nodeIdForResponder = notReestablishedChannel.RemoteNodeId
            match notReestablishedChannel.State with
            | ChannelState.Normal _ ->
                return Ok (UsableChannel txIdHex)
            | _ ->
                let channelKeysSeed = details.SerializedChannel.KeysRepoSeed
                let channelCounterpartyIP = details.SerializedChannel.CounterpartyIP

                let judgement = JudgeDepth details
                match judgement with
                | NotReady reason ->
                    return Ok (UnusableChannelWithReason (txIdHex, reason))
                | DeepEnough channelCommandAction ->
                    let! channelCommand = channelCommandAction
                    let keyRepo = SerializedChannel.UIntToKeyRepo channelKeysSeed
                    let channelEnvironment: ChannelEnvironment =
                        { Account = details.Account; NodeIdForResponder = notReestablishedChannel.RemoteNodeId; KeyRepo = keyRepo }
                    let! connectionRes = ConnectAndHandshake channelEnvironment channelCounterpartyIP
                    match connectionRes with
                    | Error err -> return Error err
                    | Ok connection ->
                        let chanCmd =
                            ChannelCommand.CreateChannelReestablish
                        let reestablishMsg, reestablishedChannel =
                            match Channel.executeCommand notReestablishedChannel chanCmd with
                            | Ok (ChannelEvent.WeSentChannelReestablish (ourChannelReestablish) as evt::[]) ->
                                let chan = Channel.applyEvent notReestablishedChannel evt
                                ourChannelReestablish, chan
                            | Ok evtList ->
                                failwith <| SPrintF1 "event was not a single WeSentChannelReestablish, it was: %A" evtList
                            | Error channelError ->
                                failwith <| SPrintF1 "could not execute channel command: %s" (channelError.ToString())

                        let stream = connection.Client.GetStream()
                        Infrastructure.LogDebug "Sending channel_reestablish..."
                        let! sentReestablishPeer = Send reestablishMsg connection.Peer stream
                        let channelWithFundingLockedSent, fundingLocked = GetFundingLockedMsg reestablishedChannel channelCommand
                        let! sentFundingLockedPeer = Send fundingLocked sentReestablishPeer stream
                        Infrastructure.LogDebug "Receiving channel_reestablish or funding_locked..."
                        let! msgRes = ReadUntilChannelMessage (keyRepo, sentFundingLockedPeer, connection.Client.GetStream())
                        match msgRes with
                        | Error (PeerDisconnected abruptly) ->
                            ReportDisconnection channelCounterpartyIP nodeIdForResponder abruptly "receiving channel_reestablish or funding_locked"
                            return Error (PeerDisconnected abruptly)
                        | Error errorMsg -> return Error errorMsg
                        | Ok (receivedChannelReestablishPeer, chanMsg) ->
                            let! fundingLockedRes =
                                match chanMsg with
                                | :? ChannelReestablish ->
                                    async {
                                        // TODO: validate channel_reestablish
                                        Infrastructure.LogDebug "Received channel_reestablish, now receiving funding_locked..."
                                        let! msgRes = ReadUntilChannelMessage (keyRepo, receivedChannelReestablishPeer, connection.Client.GetStream())
                                        match msgRes with
                                        | Error (PeerDisconnected abruptly) ->
                                            ReportDisconnection channelCounterpartyIP nodeIdForResponder abruptly "receiving funding_locked"
                                            return Error (PeerDisconnected abruptly)
                                        | Error errorMsg ->
                                            return Error errorMsg
                                        | Ok (_, chanMsg) ->
                                            match chanMsg with
                                            | :? FundingLocked as fundingLocked ->
                                                // TODO: validate funding_locked
                                                return Ok fundingLocked
                                            | _ ->
                                                let msg = SPrintF1 "channel message is not funding_locked, it is: %s" (chanMsg.GetType().Name)
                                                return Error <| StringError { Msg = msg; During = "waiting for funding_locked" }
                                    }
                                | :? FundingLocked as fundingLocked ->
                                    // LND can send funding_locked before replying to our channel_reestablish
                                    async {
                                        return Ok fundingLocked
                                    }
                                | _ ->
                                    let msg = SPrintF1 "channel message is not channel_reestablish or funding_locked, instead it is: %s" (chanMsg.GetType().Name)
                                    async {
                                        return Error <| StringError { Msg = msg; During = "reception of reply to channel_reestablish" }
                                    }
                            match fundingLockedRes with
                            | Error errorMsg -> return Error errorMsg
                            | Ok fundingLocked ->
                                match Channel.executeCommand channelWithFundingLockedSent (ApplyFundingLocked fundingLocked) with
                                | Ok ((ChannelEvent.BothFundingLocked _) as evt::[]) ->
                                    let bothFundingLockedChan = Channel.applyEvent channelWithFundingLockedSent evt
                                    let serializedChannel = {
                                        details.SerializedChannel with
                                            ChanState = bothFundingLockedChan.State
                                    }
                                    serializedChannel.SaveSerializedChannel fileName
                                    Infrastructure.LogDebug <| SPrintF1 "Channel overwritten (with funding transaction locked) at %s" fileName
                                    connection.Client.Dispose()
                                    return Ok (UsableChannel txIdHex)
                                | Error channelError ->
                                    return Error <| DNLChannelError channelError
                                | Ok (evt::[]) ->
                                    let msg = SPrintF1 "expected event BothFundingLocked, is %s" (evt.GetType().Name)
                                    return Error <| StringError { Msg = msg; During = "application of funding_locked" }
                                | Ok _ ->
                                    let msg = "expected only one event"
                                    return Error <| StringError { Msg = msg; During = "application of funding_locked" }
        }

    let AcceptTheirChannel (random: Random)
                           (account: NormalUtxoAccount)
                               : Async<Result<unit, LNError>> =
        let ip, port = "127.0.0.1", 9735
        let listener = new TcpListener (IPAddress.Parse ip, port)
        listener.Start()
        let channelKeysSeed, keyRepo, temporaryChannelId = GetSeedAndRepo random
        let ourNodeSecret = keyRepo.NodeSecret.PrivateKey
        let publicKey = ourNodeSecret.PubKey.ToBytes()
        Console.WriteLine (SPrintF3 "This node, connect to it: %s@%s:%i" (hex.EncodeData publicKey) ip port)
        async {
            use! client = listener.AcceptTcpClientAsync() |> Async.AwaitTask
            let stream = client.GetStream()
            let channelCounterpartyIP = client.Client.RemoteEndPoint :?> IPEndPoint
            // client.Client is actually a Socket (not a TcpClient), LOL
            let peerId = client.Client.RemoteEndPoint |> PeerId
            let initialPeer = Peer.CreateInbound(peerId, ourNodeSecret)

            let! act1Res = ReadExactAsync stream bolt08ActOneLength
            match act1Res with
            | Error err -> return Error err
            | Ok act1 ->
                let act1Result = PeerChannelEncryptor.processActOneWithKey act1 ourNodeSecret initialPeer.ChannelEncryptor
                match act1Result with
                | Error err ->
                    return Error <| StringError { Msg = SPrintF1 "error from DNL: %A" err; During = "processing of their act1" }
                | Ok (act2, pce) ->
                    let act2, peerWithSentAct2 =
                        act2, { initialPeer with ChannelEncryptor = pce }
                    do! stream.WriteAsync(act2, 0, act2.Length) |> Async.AwaitTask
                    let! act3Res = ReadExactAsync stream bolt08ActThreeLength
                    match act3Res with
                    | Error err -> return Error err
                    | Ok act3 ->
                        let act3Result = PeerChannelEncryptor.processActThree act3 peerWithSentAct2.ChannelEncryptor
                        match act3Result with
                        | Error err ->
                            return Error <| StringError { Msg = SPrintF1 "error from DNL: %A" err; During = "processing of their act3" }
                        | Ok (remoteNodeId, pce) ->
                            let receivedAct3Peer = { peerWithSentAct2 with ChannelEncryptor = pce }
                            Infrastructure.LogDebug "Receiving init..."
                            let! initRes = ReadAsync keyRepo receivedAct3Peer stream
                            match initRes with
                            | Error (PeerDisconnected abruptly) ->
                                ReportDisconnection channelCounterpartyIP remoteNodeId abruptly "receiving init message while accepting"
                                return Error (PeerDisconnected abruptly)
                            | Error err -> return Error err
                            | Ok init ->
                                match Peer.executeCommand receivedAct3Peer init with
                                | Error peerError ->
                                    return Error <| StringError { Msg = SPrintF1 "couldn't parse init: %s" (peerError.ToString()); During = "receiving init" }
                                | Ok (ReceivedInit (newInit, _) as evt::[]) ->
                                    let peer = Peer.applyEvent receivedAct3Peer evt
                                    let connection: Connection =
                                        { Init = newInit; Peer = peer; Client = client }
                                    let! sentInitPeer = Send plainInit connection.Peer stream
                                    Infrastructure.LogDebug "Receiving open_channel..."
                                    let! msgRes = ReadUntilChannelMessage (keyRepo, sentInitPeer, stream)
                                    match msgRes with
                                    | Error (PeerDisconnected abruptly) ->
                                        ReportDisconnection channelCounterpartyIP remoteNodeId abruptly "receiving open_channel"
                                        return Error (PeerDisconnected abruptly)
                                    | Error errorMsg -> return Error errorMsg
                                    | Ok (receivedOpenChanPeer, chanMsg) ->
                                        match chanMsg with
                                        | :? OpenChannel as openChannel ->
                                            Infrastructure.LogDebug "Creating LocalParams..."
                                            let channelKeys, localParams = GetLocalParams false openChannel.FundingSatoshis remoteNodeId account keyRepo
                                            let initFundee: InputInitFundee = {
                                                    TemporaryChannelId = temporaryChannelId
                                                    LocalParams = localParams
                                                    RemoteInit = connection.Init
                                                    ToLocal = LNMoney.MilliSatoshis 0L
                                                    ChannelKeys = channelKeys
                                                }
                                            let chanCmd = ChannelCommand.CreateInbound initFundee
                                            let fundingTxProvider (_: IDestination, _: Money, _: FeeRatePerKw) =
                                                failwith "not funding channel, so unreachable"
                                            Infrastructure.LogDebug "Creating Channel..."
                                            let! feeEstimator = FeeEstimator.Create ()
                                            let initialChan: Channel = CreateChannel
                                                                           account
                                                                           keyRepo
                                                                           feeEstimator
                                                                           ourNodeSecret
                                                                           fundingTxProvider
                                                                           Config.BitcoinNet
                                                                           remoteNodeId

                                            match Channel.executeCommand initialChan chanCmd with
                                            | Ok (NewInboundChannelStarted _ as evt::[]) ->
                                                let inboundStartedChan =
                                                    Channel.applyEvent initialChan evt

                                                Infrastructure.LogDebug "Applying open_channel..."
                                                let res = Channel.executeCommand inboundStartedChan (ApplyOpenChannel openChannel)

                                                Infrastructure.LogDebug "Generating accept_channel..."
                                                match res with
                                                | Ok (ChannelEvent.WeAcceptedOpenChannel(acceptChannel, _) as evt::[]) ->
                                                    let receivedOpenChannelChan = Channel.applyEvent inboundStartedChan evt
                                                    Infrastructure.LogDebug "Sending accept_channel..."
                                                    let! sentAcceptChanPeer = Send acceptChannel receivedOpenChanPeer stream

                                                    Infrastructure.LogDebug "Receiving funding_created..."
                                                    let! msgRes = ReadUntilChannelMessage (keyRepo, sentAcceptChanPeer, stream)
                                                    match msgRes with
                                                    | Error (PeerDisconnected abruptly) ->
                                                        let context = SPrintF2 "receiving funding_created, their open_channel == %A, our accept_channel == %A" openChannel acceptChannel
                                                        ReportDisconnection channelCounterpartyIP remoteNodeId abruptly context
                                                        return Error (PeerDisconnected abruptly)
                                                    | Error errorMsg -> return Error errorMsg
                                                    | Ok (receivedFundingCreatedPeer, chanMsg) ->
                                                        match chanMsg with
                                                        | :? FundingCreated as fundingCreated ->
                                                            match Channel.executeCommand receivedOpenChannelChan (ApplyFundingCreated fundingCreated) with
                                                            | Ok (ChannelEvent.WeAcceptedFundingCreated(fundingSigned, _) as evt::[]) ->
                                                                let receivedFundingCreatedChan = Channel.applyEvent receivedOpenChannelChan evt
                                                                let! _ = Send fundingSigned receivedFundingCreatedPeer stream

                                                                let fileName = GetNewChannelFilename()
                                                                let remoteIp = client.Client.RemoteEndPoint :?> IPEndPoint
                                                                let endpointToSave =
                                                                    if remoteIp.Address = IPAddress.Loopback then
                                                                        Infrastructure.LogDebug "WARNING: Remote address is the loopback address, saving 127.0.0.2 as IP instead!"
                                                                        IPEndPoint (IPAddress.Parse "127.0.0.2", 9735)
                                                                    else
                                                                        remoteIp
                                                                SerializedChannel.Save account receivedFundingCreatedChan channelKeysSeed endpointToSave acceptChannel.MinimumDepth fileName
                                                                Infrastructure.LogDebug <| SPrintF1 "Channel saved to %s" fileName

                                                                return Ok ()
                                                            | Ok evtList ->
                                                                return Error <| StringError { Msg = SPrintF1 "event was not a single WeAcceptedFundingCreated, it was: %A" evtList; During = "application of their funding_created message" }
                                                            | Error channelError ->
                                                                return Error <| StringError { Msg = SPrintF1 "could not apply funding_created: %s" (channelError.ToString()); During = "application of their funding_created message" }

                                                        | _ ->
                                                            return Error <| StringError { Msg = SPrintF1 "channel message is not funding_created: %s" (chanMsg.GetType().Name); During = "reception of answer to accept_channel" }

                                                | Ok evtList ->
                                                    return Error <| StringError { Msg = SPrintF1 "event list was not a single WeAcceptedOpenChannel, it was: %A" evtList; During = "generation of an accept_channel message" }
                                                | Error err ->
                                                    return Error <| StringError { Msg = SPrintF1 "error from DNL: %A" err; During = "generation of an accept_channel message" }

                                            | Ok evtList ->
                                                return Error <| StringError { Msg = SPrintF1 "event was not a single NewInboundChannelStarted, it was: %A" evtList; During = "execution of CreateChannel command" }
                                            | Error channelError ->
                                                return Error <| StringError { Msg = SPrintF1 "could not execute channel command: %s" (channelError.ToString()); During = "execution of CreateChannel command" }
                                        | _ ->
                                            return Error <| StringError { Msg = SPrintF1 "channel message is not open_channel: %s" (chanMsg.GetType().Name); During = "reception of open_channel" }
                                | Ok _ ->
                                    return Error <| StringError { Msg = "not one good ReceivedInit event"; During = "reception of init message" }

        }
