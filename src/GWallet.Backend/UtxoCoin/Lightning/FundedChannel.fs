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
open GWallet.Backend.UtxoCoin
open GWallet.Backend.UtxoCoin.Lightning.Util

open FSharp.Core

type FundedChannel = {
    ConnectedChannel: ConnectedChannel
    TheirFundingLockedMsgOpt: Option<FundingLocked>
} with
    interface IDisposable with
        member this.Dispose() =
            (this.ConnectedChannel :> IDisposable).Dispose()

    static member FundChannel (outgoingUnfundedChannel: OutgoingUnfundedChannel)
                                  : Async<Result<FundedChannel, PeerWrapper * ChannelOperationError>> = async {
        let connectedChannel = outgoingUnfundedChannel.ConnectedChannel
        let peerWrapper = connectedChannel.PeerWrapper
        let channelWrapper = connectedChannel.ChannelWrapper

        let fundingCreatedMsgRes, channelWrapper =
            let channelCmd = ApplyAcceptChannel outgoingUnfundedChannel.AcceptChannel
            channelWrapper.ExecuteCommand channelCmd <| function
                | (WeAcceptedAcceptChannel(fundingCreatedMsg, _)::[])
                    -> Some fundingCreatedMsg
                | _ -> None
        match fundingCreatedMsgRes with
        | Error err ->
            let connectedChannel = {
                connectedChannel with
                    PeerWrapper = peerWrapper
                    ChannelWrapper = channelWrapper
            }
            let! connectedChannel = connectedChannel.SendError (err.ToString())
            return Error (connectedChannel.PeerWrapper, ChannelOperationError.ChannelError err)
        | Ok fundingCreatedMsg ->
            let! peerWrapper = peerWrapper.SendMsg fundingCreatedMsg
            let! peerWrapper, channelMsgRes = peerWrapper.RecvChannelMsg()
            match channelMsgRes with
            | Error errorMessage ->
                return Error (peerWrapper, ChannelOperationError.PeerErrorMessage errorMessage)
            | Ok channelMsg ->
                let fundingSignedMsg =
                    match channelMsg with
                    | :? FundingSigned as fundingSignedMsg -> fundingSignedMsg
                    | msg -> raise <| UnexpectedMsg(["FundingSigned"], msg)
                let finalizedTxRes, channelWrapper =
                    let channelCmd = ChannelCommand.ApplyFundingSigned fundingSignedMsg
                    channelWrapper.ExecuteCommand channelCmd <| function
                        | (WeAcceptedFundingSigned(finalizedTx, _)::[]) -> Some finalizedTx
                        | _ -> None
                let connectedChannel = {
                    connectedChannel with
                        PeerWrapper = peerWrapper
                        ChannelWrapper = channelWrapper
                }
                match finalizedTxRes with
                | Error err ->
                    let! connectedChannel = connectedChannel.SendError (err.ToString())
                    return Error (connectedChannel.PeerWrapper, ChannelOperationError.ChannelError err)
                | Ok finalizedTx ->
                    connectedChannel.SaveToWallet()
                    let! _txid =
                        let signedTx: string = finalizedTx.Value.ToHex()
                        Account.BroadcastRawTransaction Currency.BTC signedTx
                    let fundedChannel = {
                        ConnectedChannel = connectedChannel
                        TheirFundingLockedMsgOpt = None
                    }
                    return Ok fundedChannel
    }

    static member AcceptChannel (peerWrapper: PeerWrapper)
                                (account: NormalUtxoAccount)
                                    : Async<Result<FundedChannel, PeerWrapper * ChannelOperationError>> = async {
        let nodeSecret = peerWrapper.NodeSecret
        let channelIndex =
            let random = Org.BouncyCastle.Security.SecureRandom() :> Random
            random.Next(1, Int32.MaxValue / 2)
        let nodeId = peerWrapper.MsgStream.TransportStream.Peer.TheirNodeId.Value
        let! peerWrapper, channelMsgRes = peerWrapper.RecvChannelMsg()
        match channelMsgRes with
        | Error errorMessage ->
            return Error (peerWrapper, ChannelOperationError.PeerErrorMessage errorMessage)
        | Ok channelMsg ->
            let openChannelMsg =
                match channelMsg with
                | :? OpenChannel as openChannelMsg -> openChannelMsg
                | msg -> raise <| UnexpectedMsg(["OpenChannel"], msg)
            let channelWrapper =
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
            let res, channelWrapper =
                let channelCmd =
                    let inputInitFundee: InputInitFundee = {
                        TemporaryChannelId = openChannelMsg.TemporaryChannelId
                        LocalParams = localParams
                        RemoteInit = peerWrapper.Init
                        ToLocal = LNMoney.MilliSatoshis 0L
                        ChannelKeys = channelKeys
                    }
                    ChannelCommand.CreateInbound inputInitFundee
                channelWrapper.ExecuteCommand channelCmd <| function
                    | (NewInboundChannelStarted(_)::[]) -> Some ()
                    | _ -> None
            let () = Unwrap res "error executing create inbound channel command"

            let acceptChannelMsgRes, channelWrapper =
                let channelCmd = ApplyOpenChannel openChannelMsg
                channelWrapper.ExecuteCommand channelCmd <| function
                    | (WeAcceptedOpenChannel(acceptChannelMsg, _)::[]) -> Some acceptChannelMsg
                    | _ -> None
            match acceptChannelMsgRes with
            | Error err ->
                let connectedChannel = {
                    PeerWrapper = peerWrapper
                    ChannelWrapper = channelWrapper
                    Account = account
                    MinimumDepth = BlockHeightOffset32 0u
                    ChannelIndex = channelIndex
                }
                let! connectedChannel = connectedChannel.SendError (err.ToString())
                return Error (connectedChannel.PeerWrapper, ChannelOperationError.ChannelError err)
            | Ok acceptChannelMsg ->
                let! peerWrapper = peerWrapper.SendMsg acceptChannelMsg
                let! peerWrapper, channelMsgRes = peerWrapper.RecvChannelMsg()
                match channelMsgRes with
                | Error errorMsg ->
                    return Error (peerWrapper, ChannelOperationError.PeerErrorMessage errorMsg)
                | Ok channelMsg ->
                    let fundingCreatedMsg =
                        match channelMsg with
                        | :? FundingCreated as fundingCreatedMsg -> fundingCreatedMsg
                        | msg -> raise <| UnexpectedMsg(["FundingCreated"], msg)

                    let fundingSignedMsgRes, channelWrapper =
                        let channelCmd = ApplyFundingCreated fundingCreatedMsg
                        channelWrapper.ExecuteCommand channelCmd <| function
                            | (WeAcceptedFundingCreated(fundingSignedMsg, _)::[]) -> Some fundingSignedMsg
                            | _ -> None
                    let minimumDepth = acceptChannelMsg.MinimumDepth
                    let connectedChannel = {
                        PeerWrapper = peerWrapper
                        ChannelWrapper = channelWrapper
                        Account = account
                        MinimumDepth = minimumDepth
                        ChannelIndex = channelIndex
                    }
                    match fundingSignedMsgRes with
                    | Error err ->
                        let! connectedChannel = connectedChannel.SendError (err.ToString())
                        return Error (connectedChannel.PeerWrapper, ChannelOperationError.ChannelError err)
                    | Ok fundingSignedMsg ->
                        connectedChannel.SaveToWallet()

                        let! peerWrapper = connectedChannel.PeerWrapper.SendMsg fundingSignedMsg
                        let connectedChannel = { connectedChannel with PeerWrapper = peerWrapper }

                        let fundedChannel = {
                            ConnectedChannel = connectedChannel
                            TheirFundingLockedMsgOpt = None
                        }
                        return Ok fundedChannel
    }

    member this.GetConfirmations(): Async<FundedChannel * BlockHeightOffset32> = async {
        let! confirmationCount =
            let txId = this.FundingTxId.Value.ToString()
            Server.Query
                Currency.BTC
                (QuerySettings.Default ServerSelectionMode.Fast)
                (ElectrumClient.GetConfirmations txId)
                None
        return this, confirmationCount |> BlockHeightOffset32
    }

    member this.FundingTxId
        with get(): TxId = this.ConnectedChannel.FundingTxId

    member this.MinimumDepth
        with get(): BlockHeightOffset32 = this.ConnectedChannel.MinimumDepth

    member this.ChannelId
        with get(): ChannelId = this.ConnectedChannel.ChannelId


