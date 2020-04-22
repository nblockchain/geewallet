namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Diagnostics

open NBitcoin

open DotNetLightning.Utils
open DotNetLightning.Serialize.Msgs
open DotNetLightning.Channel
open DotNetLightning.Transactions

open GWallet.Backend
open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks
open GWallet.Backend.UtxoCoin
open GWallet.Backend.UtxoCoin.Lightning.Util

open FSharp.Core

type FundChannelError =
    | InvalidAcceptChannel of PeerWrapper * ChannelError
    | RecvFundingSigned of RecvMsgError
    | FundingCreatedPeerErrorResponse of PeerWrapper * PeerErrorMessage
    | InvalidFundingSigned of PeerWrapper * ChannelError
    | ExpectedFundingSigned of ILightningMsg
    with
    member this.Message =
        match this with
        | InvalidAcceptChannel (_, err) ->
            SPrintF1 "Invalid accept_channel message: %s" err.Message
        | RecvFundingSigned err ->
            SPrintF1 "Error receiving funding_signed message: %s" err.Message
        | FundingCreatedPeerErrorResponse (_, err) ->
            SPrintF1 "Peer responded to our funding_created message with an error: %s" err.Message
        | InvalidFundingSigned (_, err) ->
            SPrintF1 "Invalid funding_signed message: %s" err.Message
        | ExpectedFundingSigned msg ->
            SPrintF1 "Expected funding_signed msg, got %A" (msg.GetType())
    member this.PossibleBug =
        match this with
        | RecvFundingSigned err -> err.PossibleBug
        | InvalidAcceptChannel _
        | FundingCreatedPeerErrorResponse _
        | InvalidFundingSigned _
        | ExpectedFundingSigned _ -> false

type AcceptChannelError =
    | RecvOpenChannel of RecvMsgError
    | PeerErrorMessageInsteadOfOpenChannel of PeerWrapper * PeerErrorMessage
    | InvalidOpenChannel of PeerWrapper * ChannelError
    | ExpectedOpenChannel of ILightningMsg
    | RecvFundingCreated of RecvMsgError
    | AcceptChannelPeerErrorResponse of PeerWrapper * PeerErrorMessage
    | InvalidFundingCreated of PeerWrapper * ChannelError
    | ExpectedFundingCreated of ILightningMsg
    with
    member this.Message =
        match this with
        | RecvOpenChannel err ->
            SPrintF1 "Error receiving open_channel msg: %s" err.Message
        | PeerErrorMessageInsteadOfOpenChannel (_, err) ->
            SPrintF1 "Peer sent an error message instead of open_channel: %s" err.Message
        | InvalidOpenChannel (_, err) ->
            SPrintF1 "Invalid open_channel message: %s" err.Message
        | ExpectedOpenChannel msg ->
            SPrintF1 "Expected open_channel msg, got %A" (msg.GetType())
        | RecvFundingCreated err ->
            SPrintF1 "Error receiving funding_created message: %s" err.Message
        | AcceptChannelPeerErrorResponse (_, err) ->
            SPrintF1 "Peer responded to our accept_channel message with an error: %s" err.Message
        | InvalidFundingCreated (_, err) ->
            SPrintF1 "Invalid funding_created message: %s" err.Message
        | ExpectedFundingCreated msg ->
            SPrintF1 "Expected funding_created message, got %A" (msg.GetType())
    member this.PossibleBug =
        match this with
        | RecvOpenChannel err -> err.PossibleBug
        | RecvFundingCreated err -> err.PossibleBug
        | PeerErrorMessageInsteadOfOpenChannel _
        | InvalidOpenChannel _
        | ExpectedOpenChannel _
        | AcceptChannelPeerErrorResponse _
        | InvalidFundingCreated _
        | ExpectedFundingCreated _ -> false
    
type FundedChannel = {
    ConnectedChannel: ConnectedChannel
    TheirFundingLockedMsgOpt: Option<FundingLockedMsg>
} with
    interface IDisposable with
        member this.Dispose() =
            (this.ConnectedChannel :> IDisposable).Dispose()

    static member FundChannel (outgoingUnfundedChannel: OutgoingUnfundedChannel)
                                  : Async<Result<FundedChannel, FundChannelError>> = async {
        let connectedChannel = outgoingUnfundedChannel.ConnectedChannel
        let peerWrapper = connectedChannel.PeerWrapper
        let channelWrapper = connectedChannel.ChannelWrapper

        let fundingCreatedMsgRes, channelWrapperAfterFundingCreated =
            let channelCmd = ApplyAcceptChannel outgoingUnfundedChannel.AcceptChannelMsg
            channelWrapper.ExecuteCommand channelCmd <| function
                | (WeAcceptedAcceptChannel(fundingCreatedMsg, _)::[])
                    -> Some fundingCreatedMsg
                | _ -> None
        match fundingCreatedMsgRes with
        | Error err ->
            let connectedChannelAfterError = {
                connectedChannel with
                    PeerWrapper = peerWrapper
                    ChannelWrapper = channelWrapperAfterFundingCreated
            }
            let! connectedChannelAfterErrorSent =
                connectedChannelAfterError.SendError err.Message
            return Error <| InvalidAcceptChannel
                (connectedChannelAfterErrorSent.PeerWrapper, err)
        | Ok fundingCreatedMsg ->
            let! peerWrapperAfterFundingCreated = peerWrapper.SendMsg fundingCreatedMsg
            let! recvChannelMsgRes = peerWrapperAfterFundingCreated.RecvChannelMsg()
            match recvChannelMsgRes with
            | Error (RecvMsg recvMsgRes) -> return Error <| RecvFundingSigned recvMsgRes
            | Error (ReceivedPeerErrorMessage (peerWrapperAfterFundingSigned, errorMessage)) ->
                return Error <| FundingCreatedPeerErrorResponse
                    (peerWrapperAfterFundingSigned, errorMessage)
            | Ok (peerWrapperAfterFundingSigned, channelMsg) ->
                match channelMsg with
                | :? FundingSignedMsg as fundingSignedMsg ->
                    let finalizedTxRes, channelWrapperAfterFundingSigned =
                        let channelCmd = ChannelCommand.ApplyFundingSigned fundingSignedMsg
                        channelWrapperAfterFundingCreated.ExecuteCommand channelCmd <| function
                            | (WeAcceptedFundingSigned(finalizedTx, _)::[]) -> Some finalizedTx
                            | _ -> None
                    let connectedChannelAfterFundingSigned = {
                        connectedChannel with
                            PeerWrapper = peerWrapperAfterFundingSigned
                            ChannelWrapper = channelWrapperAfterFundingSigned
                    }
                    match finalizedTxRes with
                    | Error err ->
                        let! connectedChannelAfterError =
                            connectedChannelAfterFundingSigned.SendError err.Message
                        return Error <| InvalidFundingSigned
                            (connectedChannelAfterError.PeerWrapper, err)
                    | Ok finalizedTx ->
                        connectedChannelAfterFundingSigned.SaveToWallet()
                        let! _txid =
                            let signedTx: string = finalizedTx.Value.ToHex()
                            Account.BroadcastRawTransaction Currency.BTC signedTx
                        let fundedChannel = {
                            ConnectedChannel = connectedChannelAfterFundingSigned
                            TheirFundingLockedMsgOpt = None
                        }
                        return Ok fundedChannel
                | _ -> return Error <| ExpectedFundingSigned channelMsg
    }

    static member AcceptChannel (peerWrapper: PeerWrapper)
                                (account: NormalUtxoAccount)
                                    : Async<Result<FundedChannel, AcceptChannelError>> = async {
        let nodeSecret = peerWrapper.NodeSecret
        let channelIndex =
            let random = Org.BouncyCastle.Security.SecureRandom() :> Random
            random.Next(1, Int32.MaxValue / 2)
        let nodeId = peerWrapper.RemoteNodeId
        let! recvChannelMsgRes = peerWrapper.RecvChannelMsg()
        match recvChannelMsgRes with
        | Error (RecvMsg recvMsgError) -> return Error <| RecvOpenChannel recvMsgError
        | Error (ReceivedPeerErrorMessage (peerWrapperAfterOpenChannel, errorMessage)) ->
            return Error <| PeerErrorMessageInsteadOfOpenChannel
                (peerWrapperAfterOpenChannel, errorMessage)
        | Ok (peerWrapperAfterOpenChannel, channelMsg) ->
            match channelMsg with
            | :? OpenChannelMsg as openChannelMsg ->
                let! channelWrapper =
                    let fundingTxProvider (_: IDestination, _: Money, _: FeeRatePerKw) =
                        failwith "not funding channel, so unreachable"
                    ChannelWrapper.Create
                        nodeId
                        (Account.CreatePayoutScript account)
                        nodeSecret
                        channelIndex
                        fundingTxProvider
                        WaitForInitInternal
                let channelKeys = channelWrapper.ChannelKeys
                let localParams =
                    let funding = openChannelMsg.FundingSatoshis
                    let defaultFinalScriptPubKey = Account.CreatePayoutScript account
                    channelWrapper.LocalParams funding defaultFinalScriptPubKey false
                let res, channelWrapperAfterOpenChannel =
                    let channelCmd =
                        let inputInitFundee: InputInitFundee = {
                            TemporaryChannelId = openChannelMsg.TemporaryChannelId
                            LocalParams = localParams
                            RemoteInit = peerWrapperAfterOpenChannel.InitMsg
                            ToLocal = LNMoney.MilliSatoshis 0L
                            ChannelKeys = channelKeys
                        }
                        ChannelCommand.CreateInbound inputInitFundee
                    channelWrapper.ExecuteCommand channelCmd <| function
                        | (NewInboundChannelStarted(_)::[]) -> Some ()
                        | _ -> None
                let () = Unwrap res "error executing create inbound channel command"

                let acceptChannelMsgRes, channelWrapperAfterAcceptChannel =
                    let channelCmd = ApplyOpenChannel openChannelMsg
                    channelWrapperAfterOpenChannel.ExecuteCommand channelCmd <| function
                        | (WeAcceptedOpenChannel(acceptChannelMsg, _)::[]) -> Some acceptChannelMsg
                        | _ -> None
                match acceptChannelMsgRes with
                | Error err ->
                    let connectedChannel = {
                        PeerWrapper = peerWrapperAfterOpenChannel
                        ChannelWrapper = channelWrapperAfterAcceptChannel
                        Account = account
                        MinimumDepth = BlockHeightOffset32 0u
                        ChannelIndex = channelIndex
                    }
                    let! connectedChannelAfterErrorSent = connectedChannel.SendError err.Message
                    return Error <| InvalidOpenChannel
                        (connectedChannelAfterErrorSent.PeerWrapper, err)
                | Ok acceptChannelMsg ->
                    let! peerWrapperAfterAcceptChannel = peerWrapperAfterOpenChannel.SendMsg acceptChannelMsg
                    let! recvChannelMsgRes = peerWrapperAfterAcceptChannel.RecvChannelMsg()
                    match recvChannelMsgRes with
                    | Error (RecvMsg recvMsgError) ->
                        return Error <| RecvFundingCreated recvMsgError
                    | Error (ReceivedPeerErrorMessage (peerWrapperAfterFundingCreated, errorMessage)) ->
                        return Error <| AcceptChannelPeerErrorResponse
                            (peerWrapperAfterFundingCreated, errorMessage)
                    | Ok (peerWrapperAfterFundingCreated, channelMsg) ->
                        match channelMsg with
                        | :? FundingCreatedMsg as fundingCreatedMsg ->
                            let fundingSignedMsgRes, channelWrapperAfterFundingCreated =
                                let channelCmd = ApplyFundingCreated fundingCreatedMsg
                                channelWrapperAfterAcceptChannel.ExecuteCommand channelCmd <| function
                                    | (WeAcceptedFundingCreated(fundingSignedMsg, _)::[]) -> Some fundingSignedMsg
                                    | _ -> None
                            let minimumDepth = acceptChannelMsg.MinimumDepth
                            let connectedChannelAfterFundingCreated = {
                                PeerWrapper = peerWrapperAfterFundingCreated
                                ChannelWrapper = channelWrapperAfterFundingCreated
                                Account = account
                                MinimumDepth = minimumDepth
                                ChannelIndex = channelIndex
                            }
                            match fundingSignedMsgRes with
                            | Error err ->
                                let! connectedChannelAfterErrorSent =
                                    connectedChannelAfterFundingCreated.SendError err.Message
                                return Error <| InvalidFundingCreated
                                    (connectedChannelAfterErrorSent.PeerWrapper, err)
                            | Ok fundingSignedMsg ->
                                connectedChannelAfterFundingCreated.SaveToWallet()

                                let! peerWrapperAfterFundingSigned = connectedChannelAfterFundingCreated.PeerWrapper.SendMsg fundingSignedMsg
                                let connectedChannelAfterFundingSigned = { connectedChannelAfterFundingCreated with PeerWrapper = peerWrapperAfterFundingSigned }

                                let fundedChannel = {
                                    ConnectedChannel = connectedChannelAfterFundingSigned
                                    TheirFundingLockedMsgOpt = None
                                }
                                return Ok fundedChannel
                        | _ -> return Error <| ExpectedFundingCreated channelMsg
            | _ -> return Error <| ExpectedOpenChannel channelMsg

    }

    member this.FundingScriptCoin
        with get(): ScriptCoin =
            UnwrapOption
                this.ConnectedChannel.FundingScriptCoin
                "The FundedChannel type is created by funding a channel and \
                guarantees that the underlying ChannelState has a script coin"

    member this.GetConfirmations(): Async<BlockHeightOffset32> = async {
        let! confirmationCount =
            let txId = this.FundingTxId.Value.ToString()
            QueryBTCFast (ElectrumClient.GetConfirmations txId)
        return confirmationCount |> BlockHeightOffset32
    }

    member this.GetLocationOnChain(): Async<BlockHeight * TxIndexInBlock> = async {
        let fundingScriptCoin = this.FundingScriptCoin
        let txIdHex: string = this.ConnectedChannel.FundingTxId.Value.ToString()
        let fundingDestination: TxDestination = fundingScriptCoin.ScriptPubKey.GetDestination()
        let fundingAddress: BitcoinAddress = fundingDestination.GetAddress Config.BitcoinNet
        let fundingAddressString: string = fundingAddress.ToString()
        let scriptHash = Account.GetElectrumScriptHashFromPublicAddress Currency.BTC fundingAddressString
        let! historyList =
            QueryBTCFast (ElectrumClient.GetBlockchainScriptHashHistory scriptHash)
        let history = Seq.head historyList
        let fundingBlockHeight = BlockHeight history.Height
        let! merkleResult =
            QueryBTCFast (ElectrumClient.GetBlockchainScriptHashMerkle txIdHex history.Height)
        let fundingTxIndexInBlock = TxIndexInBlock merkleResult.Pos
        return fundingBlockHeight, fundingTxIndexInBlock
    }

    member this.FundingTxId
        with get(): TxId = this.ConnectedChannel.FundingTxId

    member this.MinimumDepth
        with get(): BlockHeightOffset32 = this.ConnectedChannel.MinimumDepth

    member this.ChannelId
        with get(): ChannelId = this.ConnectedChannel.ChannelId


