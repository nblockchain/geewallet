namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Net.Sockets
open System.Net
open System.Diagnostics
open System.IO

open DotNetLightning.Utils
open DotNetLightning.Serialize
open DotNetLightning.Serialize.Msgs
open DotNetLightning.Channel
open DotNetLightning.Peer
open DotNetLightning.Crypto
open DotNetLightning.Chain
open DotNetLightning.Transactions

open GWallet.Backend
open GWallet.Backend.FSharpUtil
open GWallet.Backend.UtxoCoin
open GWallet.Backend.FSharpUtil.UwpHacks
open NBitcoin

open Newtonsoft.Json.Linq

open FSharp.Core

module Lightning =
    let private hex = DataEncoders.HexEncoder()

    // we never batch channel openings, there will only be one output
    let private fundingOutputIndex = 0us |> TxOutIndex

    let private featureBits =
        match FeatureBit.TryCreate([| 1uy <<< Feature.OptionDataLossProtect.OptionalBitPosition |]) with
        | Ok featureBits -> featureBits
        | Error err -> failwith <| SPrintF1 "could not derive FeatureBits: %A" err
    let plainInit: Init = {
        Features = featureBits
        TLVStream = [||]
    }

    let private bolt08EncryptedMessageLengthPrefixLength = 18
    // https://github.com/lightningnetwork/lightning-rfc/blob/master/08-transport.md#authenticated-key-exchange-handshake-specification
    let private bolt08ActOneLength = 50
    let private bolt08ActTwoLength = 50
    let private bolt08ActThreeLength = 66

    let private channelFilePrefix = "chan"
    let private channelFileEnding = ".json"

    type FeeEstimator(btcPerKb: decimal) =
        interface IFeeEstimator with
            member __.GetEstSatPer1000Weight(_: ConfirmationTarget) =
                let satPerKb = (Money (btcPerKb, MoneyUnit.BTC)).ToUnit MoneyUnit.Satoshi
                // 4 weight units per byte. See segwit specs.
                let satPerKiloWeightUnit = satPerKb * 4m |> Convert.ToUInt32
                FeeRatePerKw satPerKiloWeightUnit

    let MakeFeeEstimator (account: IAccount): Async<IFeeEstimator> = async {
        let averageFee (feesFromDifferentServers: List<decimal>): decimal =
            let sum: decimal = List.sum feesFromDifferentServers
            let avg = sum / decimal feesFromDifferentServers.Length
            avg
        let estimateFeeJob = ElectrumClient.EstimateFee 2 // same confirmation target as in UtxoCoinAccount
        let! btcPerKb = Server.Query account.Currency (QuerySettings.FeeEstimation averageFee) estimateFeeJob None
        return FeeEstimator btcPerKb :> IFeeEstimator
    }

    let rec ReadExactAsync (stream: NetworkStream) (numberBytesToRead: int): Async<array<byte>> =
        async {
            let buf: array<byte> = Array.zeroCreate numberBytesToRead
            let! numberBytesRead = stream.ReadAsync(buf, 0, numberBytesToRead) |> Async.AwaitTask
            if numberBytesRead < numberBytesToRead then
                let beginning: array<byte> = buf.AsSpan().Slice(0, numberBytesRead).ToArray()
                let! rest = ReadExactAsync stream (numberBytesToRead - numberBytesRead)
                let concatenated: array<byte> = Array.concat [| beginning; rest |]
                return concatenated
            else
                return buf
        }

    let ReadAsync (keyRepo: DefaultKeyRepository) (peer: Peer) (stream: NetworkStream): Async<PeerCommand> =
        match peer.ChannelEncryptor.GetNoiseStep() with
        | ActTwo ->
            async {
                let! actTwo = ReadExactAsync stream bolt08ActTwoLength
                return ProcessActTwo(actTwo, keyRepo.NodeSecret.PrivateKey)
            }
        | ActThree ->
            async {
                let! actThree = ReadExactAsync stream bolt08ActThreeLength
                return ProcessActThree actThree
            }
        | NoiseComplete ->
            async {
                let! encryptedLength = ReadExactAsync stream bolt08EncryptedMessageLengthPrefixLength
                let reader length =
                    let buf = Array.zeroCreate length
                    Debug.Assert ((stream.Read (buf, 0, length)) = length, "read length not equal to requested length")
                    buf
                return DecodeCipherPacket (encryptedLength, reader)
            }
        | _ ->
            failwith "unreachable"

    type MessageReceived =
    | ChannelMessage of Peer * IChannelMsg
    | ErrorMessage of Peer * ErrorMessage
    | OtherMessage of Peer

    let ProcessPeerEvents (oldPeer: Peer) (peerEventsResult: Result<List<PeerEvent>,'a>): MessageReceived =
        match peerEventsResult with
        | Ok (evt::[]) ->
            match evt with
            | PeerEvent.ReceivedChannelMsg (chanMsg, _) ->
                ChannelMessage (Peer.applyEvent oldPeer evt, chanMsg)
            | PeerEvent.ReceivedError (error, _) ->
                MessageReceived.ErrorMessage (Peer.applyEvent oldPeer evt, error)
            | _ ->
                DebugLogger <| SPrintF1 "Warning: ignoring event that was not ReceivedChannelMsg, it was: %s" (evt.GetType().Name)
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

    let ConnectAndHandshake ({ Account = _; NodeIdForResponder = nodeIdForResponder; KeyRepo = keyRepo }: ChannelEnvironment)
                            (channelCounterpartyIP: IPEndPoint)
                                : Async<Connection> =
        async {
            let responderId = channelCounterpartyIP :> EndPoint |> PeerId
            let initialPeer = Peer.CreateOutbound(responderId, nodeIdForResponder, keyRepo.NodeSecret.PrivateKey)
            let act1, peerEncryptor = PeerChannelEncryptor.getActOne initialPeer.ChannelEncryptor
            Debug.Assert((bolt08ActOneLength = act1.Length), "act1 has wrong length")
            let sentAct1Peer = { initialPeer with ChannelEncryptor = peerEncryptor }

            let client = new TcpClient (channelCounterpartyIP.AddressFamily)
            DebugLogger <| SPrintF1 "Connecting over TCP to %A..." channelCounterpartyIP
            do! client.ConnectAsync(channelCounterpartyIP.Address, channelCounterpartyIP.Port) |> Async.AwaitTask
            let stream = client.GetStream()
            do! stream.WriteAsync(act1, 0, act1.Length) |> Async.AwaitTask

            // Receive act2
            DebugLogger "Receiving Act 2..."
            let! res = ReadAsync keyRepo sentAct1Peer stream
            let actThree, receivedAct2Peer =
                match Peer.executeCommand sentAct1Peer res with
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
            DebugLogger "Receiving init..."
            let! res = ReadAsync keyRepo sentInitPeer stream
            return
                match Peer.executeCommand sentInitPeer res with
                | Ok (ReceivedInit (newInit, _) as evt::[]) ->
                    let peer = Peer.applyEvent sentInitPeer evt
                    { Init = newInit; Peer = peer; Client = client }
                | Ok _ ->
                    failwith "not one good ReceivedInit event"
                | Error peerError ->
                    failwith <| SPrintF1 "couldn't parse init: %s" (peerError.ToString())
        }

    let rec ReadUntilChannelMessage (keyRepo: DefaultKeyRepository, peer: Peer, stream: NetworkStream): Async<Result<Peer * IChannelMsg, ErrorMessage>> =
        async {
            let! res = ReadAsync keyRepo peer stream
            let messageReceived =
                res
                |> Peer.executeCommand peer
                |> ProcessPeerEvents peer
            match messageReceived with
            | ChannelMessage (newPeer, chanMsg) ->
                return Ok (newPeer, chanMsg)
            | MessageReceived.ErrorMessage (_, errorMsg) ->
                return Error errorMsg
            | OtherMessage newPeer ->
                return! ReadUntilChannelMessage (keyRepo, newPeer, stream)
        }

    let GetLocalParams (isFunder: Boolean) (nodeIdForResponder: NodeId) (account: NormalUtxoAccount) (keyRepo: DefaultKeyRepository): ChannelKeys * LocalParams =
        let channelKeys: ChannelKeys = (keyRepo :> IKeysRepository).GetChannelKeys false
        let channelPubkeys: ChannelPubKeys = channelKeys.ToChannelPubKeys()
        channelKeys, {
            Features = featureBits
            NodeId = nodeIdForResponder
            ChannelPubKeys = channelPubkeys
            DustLimitSatoshis = Money 5UL
            MaxHTLCValueInFlightMSat = LNMoney 5000L
            ChannelReserveSatoshis = Money 1000L
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
                             : Async<Result<AcceptChannel * Channel * Peer, ErrorMessage>> =
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
            (fundingTransaction |> FinalizedTx, fundingOutputIndex) |> Ok

        let channelKeys, localParams = GetLocalParams true nodeIdForResponder account keyRepo

        async {
            let! feeEstimator = MakeFeeEstimator account
            let initFunder =
                {
                    InputInitFunder.PushMSat = LNMoney.MilliSatoshis 0L
                    TemporaryChannelId = temporaryChannelId
                    FundingSatoshis = Money (channelCapacity.ValueToSend, MoneyUnit.BTC)
                    InitFeeRatePerKw = feeEstimator.GetEstSatPer1000Weight <| ConfirmationTarget.Normal
                    FundingTxFeeRatePerKw = feeEstimator.GetEstSatPer1000Weight <| ConfirmationTarget.Normal
                    LocalParams = localParams
                    RemoteInit = receivedInit
                    ChannelFlags = 0uy
                    ChannelKeys = channelKeys
                }

            let chanCmd = ChannelCommand.CreateOutbound initFunder
            let stream = client.GetStream()
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
            DebugLogger "Receiving accept_channel..."
            let! msgRes = ReadUntilChannelMessage (keyRepo, sentOpenChanPeer, stream)
            match msgRes with
            | Error errorMsg -> return Error errorMsg
            | Ok (receivedOpenChanReplyPeer, chanMsg) ->
                let acceptChannel =
                    match chanMsg with
                    | :? AcceptChannel as acceptChannel ->
                        acceptChannel
                    | _ ->
                        failwith <| SPrintF1 "channel message is not accept channel: %s" (chanMsg.GetType().Name)
                return Ok (acceptChannel, sentOpenChan, receivedOpenChanReplyPeer)
        }

    let ContinueFromAcceptChannel (keyRepo: DefaultKeyRepository)
                                  (acceptChannel: AcceptChannel)
                                  (sentOpenChan: Channel)
                                  (stream: NetworkStream)
                                  (receivedOpenChanReplyPeer: Peer)
                                      : Async<Result<string * Channel, ErrorMessage>> =
        async {
            let fundingCreated, receivedAcceptChannelChan =
                match Channel.executeCommand sentOpenChan (ApplyAcceptChannel acceptChannel) with
                | Ok (ChannelEvent.WeAcceptedAcceptChannel(fundingCreated, _) as evt::[]) ->
                    let chan = Channel.applyEvent sentOpenChan evt
                    fundingCreated, chan
                | Ok evtList ->
                    failwith <| SPrintF1 "event was not a single WeAcceptedAcceptChannel, it was: %A" evtList
                | Error channelError ->
                    failwith <| SPrintF1 "could not apply accept_channel message: %s" (channelError.ToString())

            let! sentFundingCreatedPeer = Send fundingCreated receivedOpenChanReplyPeer stream

            DebugLogger "Receiving funding_created..."
            let! msgRes = ReadUntilChannelMessage (keyRepo, sentFundingCreatedPeer, stream)
            match msgRes with
            | Error errorMsg -> return Error errorMsg
            | Ok (_, chanMsg) ->
                let fundingSigned =
                    match chanMsg with
                    | :? FundingSigned as fundingSigned ->
                        fundingSigned
                    | _ ->
                        failwith <| SPrintF1 "channel message is not funding signed: %s" (chanMsg.GetType().Name)

                let chanCmd = ChannelCommand.ApplyFundingSigned fundingSigned
                let chanEvents = Channel.executeCommand receivedAcceptChannelChan chanCmd
                let chan, finalizedTx =
                    match chanEvents with
                    | Ok (ChannelEvent.WeAcceptedFundingSigned (finalizedTx, _) as evt::[]) ->
                        let chan = Channel.applyEvent receivedAcceptChannelChan evt
                        chan, finalizedTx
                    | Ok evt ->
                        failwith <| SPrintF1 "not one good WeAcceptedFundingSigned chan evt: %s" (evt.GetType().Name)
                    | Error e ->
                        failwith <| SPrintF1 "bad result when expecting WeAcceptedFundingSigned: %s" (e.ToString())
                let signedTx: string = finalizedTx.Value.ToHex()
                let! txId = Account.BroadcastRawTransaction Currency.BTC signedTx
                return Ok (txId, chan)
        }

    let GetSeedAndRepo (random: Random): uint256 * DefaultKeyRepository * ChannelId =
        let channelKeysSeedBytes = Array.zeroCreate 32
        random.NextBytes channelKeysSeedBytes
        let channelKeysSeed = uint256 channelKeysSeedBytes
        let keyRepo = JsonMarshalling.UIntToKeyRepo channelKeysSeed
        let temporaryChannelIdBytes: array<byte> = Array.zeroCreate 32
        random.NextBytes temporaryChannelIdBytes
        let temporaryChannelId =
            temporaryChannelIdBytes
            |> uint256
            |> ChannelId
        channelKeysSeed, keyRepo, temporaryChannelId

    let GetNewChannelFilename(): string =
        channelFilePrefix
            // this offset is the approximate time this feature was added (making filenames shorter)
            + (DateTimeOffset.Now.ToUnixTimeSeconds() - 1574212362L |> string)
            + channelFileEnding

    let ContinueFromAcceptChannelAndSave (account: UtxoCoin.NormalUtxoAccount)
                                         (channelKeysSeed: uint256)
                                         (channelCounterpartyIP: IPEndPoint)
                                         (acceptChannel: AcceptChannel)
                                         (chan: Channel)
                                         (stream: NetworkStream)
                                         (peer: Peer)
                                             : Async<Result<string, ErrorMessage>> = // TxId of Funding Transaction is returned
        async {
            let keyRepo = JsonMarshalling.UIntToKeyRepo channelKeysSeed
            let! res = ContinueFromAcceptChannel keyRepo acceptChannel chan stream peer
            match res with
            | Error errorMsg -> return Error errorMsg
            | Ok (fundingTxId, receivedFundingSignedChan) ->
                let fileName = GetNewChannelFilename()
                JsonMarshalling.Save account receivedFundingSignedChan channelKeysSeed channelCounterpartyIP acceptChannel.MinimumDepth fileName
                DebugLogger <| SPrintF1 "Channel saved to %s" fileName
                return Ok fundingTxId
        }

    type NotReadyReason =
        | NeedMoreConfirmations of BlockHeightOffset32 * BlockHeightOffset32

    type ChannelMessageOrDeepEnough =
        | NotReady of NotReadyReason
        | DeepEnough of ChannelCommand

    let MaybeChannelMessageFromConfirmationCountAndHistoryAndChannel (_: string)
                                                                     (confirmationCount: BlockHeightOffset32)
                                                                     (absoluteBlockHeight: BlockHeight)
                                                                     (minSafeDepth: BlockHeightOffset32)
                                                                         : ChannelMessageOrDeepEnough =
        if confirmationCount < minSafeDepth then
            NotReady <| NeedMoreConfirmations (confirmationCount, minSafeDepth)
        else
            let txIndex: TxIndexInBlock = TxIndexInBlock 0u // DOTNETLN does not batch
            let depth: BlockHeightOffset32 = BlockHeightOffset32 <| uint32 confirmationCount.Value
            let channelCommand = ChannelCommand.ApplyFundingConfirmedOnBC (absoluteBlockHeight, txIndex, depth)
            DeepEnough channelCommand

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

    let GetConfirmationsAndHistoryAndChannel (fileName: string): Async<string * BlockHeightOffset32 * BlockHeight * Channel * UtxoCoin.NormalUtxoAccount * SerializedChannel> =
        let serializedChannel = JsonMarshalling.LoadSerializedChannel fileName
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
            let! feeEstimator = MakeFeeEstimator account

            let channel =
                JsonMarshalling.ChannelFromSerialized
                    serializedChannel
                    (CreateChannel account)
                    feeEstimator

            let txId: ChannelId =
                match channel.State with
                | ChannelState.WaitForFundingConfirmed waitForFundingConfirmedData ->
                    waitForFundingConfirmedData.ChannelId
                | ChannelState.Normal normalData ->
                    normalData.ChannelId
                | _ as unknownState ->
                    // using failwith because this should never happen
                    failwith <| SPrintF1 "unexpected saved channel state: %s" (unknownState.GetType().Name)

            let txIdHex: string = txId.Value.ToString()
            let! verboseTransactionInfo =
                Server.Query Currency.BTC (QuerySettings.Default ServerSelectionMode.Fast) (ElectrumClient.GetBlockchainTransactionVerbose txIdHex) None
            let confirmationsCount = BlockHeightOffset32 <| uint32 verboseTransactionInfo.Confirmations
            let! currentHeadersSubscriptionResult =
                Server.Query Currency.BTC (QuerySettings.Default ServerSelectionMode.Fast) (ElectrumClient.SubscribeHeaders ()) None
            let currentAbsoluteHeight: int = currentHeadersSubscriptionResult.Height
            let fundingAbsoluteHeight: int = currentAbsoluteHeight - verboseTransactionInfo.Confirmations
            let fundingBlockHeight = BlockHeight <| uint32 fundingAbsoluteHeight
            return txIdHex, confirmationsCount, fundingBlockHeight, channel, account, serializedChannel
        }

    type ChannelStatus =
    // first element is always opening transaction id
    | UsableChannel of string
    | UnusableChannelWithReason of string * NotReadyReason

    let LoadChannelAndCheckChannelMessage (fileName: string): Async<Result<ChannelStatus, ErrorMessage>> =
        async {
            let! txIdHex, confirmationsCount, absoluteBlockHeight, notReestablishedChannel, account, serializedChannel = GetConfirmationsAndHistoryAndChannel fileName
            match notReestablishedChannel.State with
            | ChannelState.Normal _ ->
                return Ok (UsableChannel txIdHex)
            | _ ->
                let channelKeysSeed = serializedChannel.KeysRepoSeed
                let channelCounterpartyIP = serializedChannel.CounterpartyIP
                let maybeChannelMessage = MaybeChannelMessageFromConfirmationCountAndHistoryAndChannel txIdHex confirmationsCount absoluteBlockHeight serializedChannel.MinSafeDepth
                match maybeChannelMessage with
                | NotReady reason ->
                    return Ok (UnusableChannelWithReason (txIdHex, reason))
                | DeepEnough channelCommand ->
                    let keyRepo = JsonMarshalling.UIntToKeyRepo channelKeysSeed
                    let channelEnvironment: ChannelEnvironment =
                        { Account = account; NodeIdForResponder = notReestablishedChannel.RemoteNodeId; KeyRepo = keyRepo }
                    let! connection = ConnectAndHandshake channelEnvironment channelCounterpartyIP
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
                    DebugLogger "Sending channel_reestablish..."
                    let! sentReestablishPeer = Send reestablishMsg connection.Peer stream
                    let channelWithFundingLockedSent, fundingLocked = GetFundingLockedMsg reestablishedChannel channelCommand
                    let! sentFundingLockedPeer = Send fundingLocked sentReestablishPeer stream
                    DebugLogger "Receiving channel_reestablish or funding_locked..."
                    let! msgRes = ReadUntilChannelMessage (keyRepo, sentFundingLockedPeer, connection.Client.GetStream())
                    match msgRes with
                    | Error errorMsg -> return Error errorMsg
                    | Ok (receivedChannelReestablishPeer, chanMsg) ->
                        let! fundingLockedRes =
                            match chanMsg with
                            | :? ChannelReestablish ->
                                async {
                                    // TODO: validate channel_reestablish
                                    DebugLogger "Received channel_reestablish, now receiving funding_locked..."
                                    let! msgRes = ReadUntilChannelMessage (keyRepo, receivedChannelReestablishPeer, connection.Client.GetStream())
                                    match msgRes with
                                    | Error errorMsg -> return Error errorMsg
                                    | Ok (_, chanMsg) ->
                                        return
                                            match chanMsg with
                                            | :? FundingLocked as fundingLocked ->
                                                // TODO: validate funding_locked
                                                Ok fundingLocked
                                            | _ ->
                                                failwith <| SPrintF1 "channel message is not funding_locked, it is: %s" (chanMsg.GetType().Name)
                                }
                            | :? FundingLocked as fundingLocked ->
                                // LND can send funding_locked before replying to our channel_reestablish
                                async {
                                    return Ok fundingLocked
                                }
                            | _ ->
                                failwith <| SPrintF1 "channel message is not channel_reestablish or funding_locked, instead it is: %s" (chanMsg.GetType().Name)
                        match fundingLockedRes with
                        | Error errorMsg -> return Error errorMsg
                        | Ok fundingLocked ->
                            let bothFundingLockedChan =
                                match Channel.executeCommand channelWithFundingLockedSent (ApplyFundingLocked fundingLocked) with
                                | Ok ((ChannelEvent.BothFundingLocked _) as evt::[]) ->
                                    Channel.applyEvent channelWithFundingLockedSent evt
                                | _ ->
                                    failwith "bad event during application of funding_locked"
                            JsonMarshalling.SaveSerializedChannel { serializedChannel with ChanState = bothFundingLockedChan.State } fileName
                            DebugLogger <| SPrintF1 "Channel overwritten (with funding transaction locked) at %s" fileName
                            connection.Client.Dispose()
                            return Ok (UsableChannel txIdHex)
        }

    let ExtractChannelNumber (path: string): Option<string * int> =
        let fileName = Path.GetFileName path
        let withoutPrefix = fileName.Substring channelFilePrefix.Length
        let withoutEnding = withoutPrefix.Substring (0, withoutPrefix.Length - channelFileEnding.Length)
        match Int32.TryParse withoutEnding with
        | true, channelNumber ->
            Some (path, channelNumber)
        | false, _ ->
            None

    let ListSavedChannels (): seq<string * int> =
        if JsonMarshalling.LightningDir.Exists then
            let files =
                Directory.GetFiles
                    ((JsonMarshalling.LightningDir.ToString()), channelFilePrefix + "*" + channelFileEnding)
            files |> Seq.choose ExtractChannelNumber
        else
            Seq.empty

    let AcceptTheirChannel (random: Random)
                           (account: NormalUtxoAccount)
                               : Async<Result<unit, ErrorMessage>> =
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
            // client.Client is actually a Socket (not a TcpClient), LOL
            let peerId = client.Client.RemoteEndPoint |> PeerId
            let initialPeer = Peer.CreateInbound(peerId, ourNodeSecret)

            let! act1 = ReadExactAsync stream bolt08ActOneLength
            let act1Result = PeerChannelEncryptor.processActOneWithKey act1 ourNodeSecret initialPeer.ChannelEncryptor
            let act2, peerWithSentAct2 =
                match act1Result with
                | Ok (act2, pce) ->
                    act2, { initialPeer with ChannelEncryptor = pce }
                | _ ->
                    failwith "bad act1"
            do! stream.WriteAsync(act2, 0, act2.Length) |> Async.AwaitTask
            let! act3 = ReadExactAsync stream bolt08ActThreeLength
            let act3Result = PeerChannelEncryptor.processActThree act3 peerWithSentAct2.ChannelEncryptor
            let remoteNodeId, pce =
                match act3Result with
                | Ok (nodeId, pce) ->
                    nodeId, pce
                | _ -> failwith "bad act3"
            let receivedAct3Peer = { peerWithSentAct2 with ChannelEncryptor = pce }
            DebugLogger "Receiving init..."
            let! res = ReadAsync keyRepo receivedAct3Peer stream
            let connection: Connection =
                match Peer.executeCommand receivedAct3Peer res with
                | Ok (ReceivedInit (newInit, _) as evt::[]) ->
                    let peer = Peer.applyEvent receivedAct3Peer evt
                    { Init = newInit; Peer = peer; Client = client }
                | Ok _ ->
                    failwith "not one good ReceivedInit event"
                | Error peerError ->
                    failwith <| SPrintF1 "couldn't parse init: %s" (peerError.ToString())

            let! sentInitPeer = Send plainInit connection.Peer stream
            DebugLogger "Receiving open_channel..."
            let! msgRes = ReadUntilChannelMessage (keyRepo, sentInitPeer, stream)
            match msgRes with
            | Error errorMsg -> return Error errorMsg
            | Ok (receivedOpenChanPeer, chanMsg) ->
                let openChannel =
                    match chanMsg with
                    | :? OpenChannel as openChannel ->
                        openChannel
                    | _ ->
                        failwith <| SPrintF1 "channel message is not open_channel: %s" (chanMsg.GetType().Name)

                DebugLogger "Creating LocalParams..."
                let channelKeys, localParams = GetLocalParams false remoteNodeId account keyRepo
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
                DebugLogger "Creating Channel..."
                let! feeEstimator = MakeFeeEstimator account
                let initialChan: Channel = CreateChannel
                                               account
                                               keyRepo
                                               feeEstimator
                                               ourNodeSecret
                                               fundingTxProvider
                                               Config.BitcoinNet
                                               remoteNodeId

                let inboundStartedChan =
                    match Channel.executeCommand initialChan chanCmd with
                    | Ok (NewInboundChannelStarted _ as evt::[]) ->
                        Channel.applyEvent initialChan evt
                    | Ok evtList ->
                        failwith <| SPrintF1 "event was not a single NewInboundChannelStarted, it was: %A" evtList
                    | Error channelError ->
                        failwith <| SPrintF1 "could not execute channel command: %s" (channelError.ToString())

                DebugLogger "Applying open_channel..."
                let res = Channel.executeCommand inboundStartedChan (ApplyOpenChannel openChannel)

                DebugLogger "Generating accept_channel..."
                let (acceptChannel: AcceptChannel), (receivedOpenChannelChan: Channel) =
                    match res with
                    | Ok (ChannelEvent.WeAcceptedOpenChannel(acceptChannel, _) as evt::[]) ->
                        let chan = Channel.applyEvent inboundStartedChan evt
                        acceptChannel, chan
                    | Ok evtList ->
                        failwith <| SPrintF1 "event list was not a single WeAcceptedOpenChannel, it was: %A" evtList
                    | Error _ ->
                        // be careful with channelError.ToString. When RResult.Describe was still used in DNL, it was observed to throw!
                        failwith "unknown error tree during application of open_channel"

                DebugLogger "Sending accept_channel..."
                let! sentAcceptChanPeer = Send acceptChannel receivedOpenChanPeer stream

                DebugLogger "Receiving funding_created..."
                let! msgRes = ReadUntilChannelMessage (keyRepo, sentAcceptChanPeer, stream)
                match msgRes with
                | Error errorMsg -> return Error errorMsg
                | Ok (receivedFundingCreatedPeer, chanMsg) ->
                    let fundingCreated =
                        match chanMsg with
                        | :? FundingCreated as fundingCreated ->
                            fundingCreated
                        | _ ->
                            failwith <| SPrintF1 "channel message is not funding created: %s" (chanMsg.GetType().Name)

                    let fundingSigned, receivedFundingCreatedChan =
                        match Channel.executeCommand receivedOpenChannelChan (ApplyFundingCreated fundingCreated) with
                        | Ok (ChannelEvent.WeAcceptedFundingCreated(fundingSigned, _) as evt::[]) ->
                            let chan = Channel.applyEvent receivedOpenChannelChan evt
                            fundingSigned, chan
                        | Ok evtList ->
                            failwith <| SPrintF1 "event was not a single WeAcceptedFundingCreated, it was: %A" evtList
                        | Error channelError ->
                            failwith <| SPrintF1 "could not apply funding_created: %s" (channelError.ToString())

                    let! _ = Send fundingSigned receivedFundingCreatedPeer stream

                    let fileName = GetNewChannelFilename()
                    let remoteIp = client.Client.RemoteEndPoint :?> IPEndPoint
                    let endpointToSave =
                        if remoteIp.Address = IPAddress.Loopback then
                            DebugLogger "WARNING: Remote address is the loopback address, saving 127.0.0.2 as IP instead!"
                            IPEndPoint (IPAddress.Parse "127.0.0.2", 9735)
                        else
                            remoteIp
                    JsonMarshalling.Save account receivedFundingCreatedChan channelKeysSeed endpointToSave acceptChannel.MinimumDepth fileName
                    DebugLogger <| SPrintF1 "Channel saved to %s" fileName

                    return Ok ()
        }
