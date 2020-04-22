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

type ActiveChannel = {
    ConnectedChannel: ConnectedChannel
} with
    interface IDisposable with
        member this.Dispose() =
            (this.ConnectedChannel :> IDisposable).Dispose()

    static member LockFunding (fundedChannel: FundedChannel)
                              (absoluteBlockHeight: BlockHeight)
                              (confirmationCount: BlockHeightOffset32)
                                  : Async<Result<ActiveChannel, BrokenChannel * ChannelOperationError>> = async {
        let theirFundingLockedMsgOpt = fundedChannel.TheirFundingLockedMsgOpt
        let connectedChannel = fundedChannel.ConnectedChannel
        if confirmationCount < connectedChannel.MinimumDepth then
            failwith
                "LockFunding called when required confirmation depth has not been reached"
        let peerWrapper = connectedChannel.PeerWrapper
        let channelWrapper = connectedChannel.ChannelWrapper
        let ourFundingLockedMsgRes, channelWrapper =
            // TODO: This looks wrong. It seems to be asserting that the
            // funding transaction exists at index 0 in the block in which it's
            // included.
            let txIndex = TxIndexInBlock 0u

            let channelCmd =
                ChannelCommand.ApplyFundingConfirmedOnBC(
                    absoluteBlockHeight,
                    txIndex,
                    confirmationCount
                )
            channelWrapper.ExecuteCommand channelCmd <| function
                | (FundingConfirmed _)::(WeSentFundingLocked fundingLockedMsg)::[] ->
                    Some fundingLockedMsg
                | _ -> None
        match ourFundingLockedMsgRes with
        | Error err ->
            let connectedChannel = {
                connectedChannel with
                    PeerWrapper = peerWrapper
                    ChannelWrapper = channelWrapper
            }
            let! connectedChannel = connectedChannel.SendError (err.ToString())
            let brokenChannel = { BrokenChannel.ConnectedChannel = connectedChannel }
            return Error (brokenChannel, ChannelOperationError.ChannelError err)
        | Ok ourFundingLockedMsg ->
            let! peerWrapper = peerWrapper.SendMsg ourFundingLockedMsg

            let! theirFundingLockedMsgRes = async {
                match theirFundingLockedMsgOpt with
                | Some theirFundingLockedMsg -> return Ok (peerWrapper, theirFundingLockedMsg)
                | None ->
                    let! peerWrapper, channelMsgRes = peerWrapper.RecvChannelMsg()
                    match channelMsgRes with
                    | Error errorMessage ->
                        let connectedChannel = {
                            connectedChannel with
                                PeerWrapper = peerWrapper
                                ChannelWrapper = channelWrapper
                        }
                        let brokenChannel = { BrokenChannel.ConnectedChannel = connectedChannel }
                        return Error (brokenChannel, ChannelOperationError.PeerErrorMessage errorMessage)
                    | Ok channelMsg ->
                        match channelMsg with
                        | :? FundingLocked as fundingLockedMsg ->
                            return Ok (peerWrapper, fundingLockedMsg)
                        | msg -> return raise <| UnexpectedMsg(["FundingLocked"], msg)
            }

            match theirFundingLockedMsgRes with
            | Error err -> return Error err
            | Ok (peerWrapper, theirFundingLockedMsg) ->
                let res, channelWrapper =
                    let channelCmd = ApplyFundingLocked theirFundingLockedMsg
                    channelWrapper.ExecuteCommand channelCmd <| function
                        | (BothFundingLocked _)::[] -> Some ()
                        | _ -> None
                let connectedChannel = {
                    connectedChannel with
                        PeerWrapper = peerWrapper
                        ChannelWrapper = channelWrapper
                }
                match res with
                | Error err ->
                    let! connectedChannel = connectedChannel.SendError (err.ToString())
                    let brokenChannel = { BrokenChannel.ConnectedChannel = connectedChannel }
                    return Error (brokenChannel, ChannelOperationError.ChannelError err)
                | Ok () ->
                    connectedChannel.SaveToWallet()
                    let activeChannel: ActiveChannel = {
                        ConnectedChannel = connectedChannel
                    }
                    return Ok activeChannel
    }

    static member WaitForFundingLocked (fundedChannel: FundedChannel)
                                           : Async<Result<ActiveChannel, BrokenChannel * ChannelOperationError>> = async {
        let rec waitForRequiredConfirmations() = async {
            let! fundedChannel, confirmationCount = fundedChannel.GetConfirmations()
            if confirmationCount < fundedChannel.MinimumDepth then
                let remainingConfirmations = fundedChannel.MinimumDepth - confirmationCount
                Console.WriteLine(SPrintF1 "Waiting for %i more confirmations." remainingConfirmations.Value)
                let sleepTime =
                    int(TimeSpan.FromMinutes(5.0).TotalMilliseconds) * int remainingConfirmations.Value
                do! Async.Sleep sleepTime
                return! waitForRequiredConfirmations()
            else
                Console.WriteLine("Funding confirmed.")
                let! currentHeadersSubscriptionResult =
                    Server.Query
                        Currency.BTC
                        (QuerySettings.Default ServerSelectionMode.Fast)
                        (ElectrumClient.SubscribeHeaders ())
                        None
                // TODO: there is a race condition here if a new block gets mined
                // between the two electrum requests
                let currentBlockHeight =
                    currentHeadersSubscriptionResult.Height |> uint32 |> BlockHeight
                let fundingBlockHeight = currentBlockHeight - confirmationCount
                return confirmationCount, fundingBlockHeight
        }
        let! confirmationCount, fundingBlockHeight = waitForRequiredConfirmations()
        return! ActiveChannel.LockFunding fundedChannel fundingBlockHeight confirmationCount
    }

    static member Reestablish (transportListener: TransportListener)
                              (channelId: ChannelId)
                              (initiateConnection: bool)
                                  : Async<Result<ActiveChannel, BrokenChannel * ChannelOperationError>> = async {
        let! connectedChannel =
            ConnectedChannel.LoadFromWallet transportListener channelId initiateConnection
        let channelWrapper = connectedChannel.ChannelWrapper
        match channelWrapper.Channel.State with
        | WaitForFundingConfirmed state ->
            let fundedChannel = {
                FundedChannel.ConnectedChannel = connectedChannel
                TheirFundingLockedMsgOpt = state.Deferred
            }
            return! ActiveChannel.WaitForFundingLocked fundedChannel
        | WaitForFundingLocked _ ->
            let fundedChannel = {
                FundedChannel.ConnectedChannel = connectedChannel
                TheirFundingLockedMsgOpt = None
            }
            return! ActiveChannel.WaitForFundingLocked fundedChannel
        | ChannelState.Normal _ ->
            let activeChannel = {
                ActiveChannel.ConnectedChannel = connectedChannel
            }
            return Ok activeChannel
        | _ ->
            return failwith <| SPrintF1 "unexpected channel state: %A" channelWrapper.Channel.State
    }

    member this.Balance
        with get(): LNMoney = this.ConnectedChannel.ChannelWrapper.Balance().Value

    member this.SpendableBalance
        with get(): LNMoney = this.ConnectedChannel.ChannelWrapper.SpendableBalance().Value

    member this.ChannelId
        with get(): ChannelId = this.ConnectedChannel.ChannelId

    member private this.SendCommit(): Async<Result<ActiveChannel, BrokenChannel * ChannelOperationError>> = async {
        let connectedChannel = this.ConnectedChannel
        let peerWrapper = connectedChannel.PeerWrapper
        let channelWrapper = connectedChannel.ChannelWrapper

        let ourCommitmentSignedMsgRes, channelWrapper =
            let channelCmd = ChannelCommand.SignCommitment
            channelWrapper.ExecuteCommand channelCmd <| function
                | (WeAcceptedCMDSign(msg, _))::[] -> Some msg
                | _ -> None
        let ourCommitmentSignedMsg = Unwrap ourCommitmentSignedMsgRes "error executing sign commit command"

        let! peerWrapper = peerWrapper.SendMsg ourCommitmentSignedMsg

        let! peerWrapper, channelMsgRes = peerWrapper.RecvChannelMsg()
        match channelMsgRes with
        | Error errorMessage ->
            let connectedChannel = {
                connectedChannel with
                    PeerWrapper = peerWrapper
                    ChannelWrapper = channelWrapper
            }
            let brokenChannel = { BrokenChannel.ConnectedChannel = connectedChannel }
            return Error (brokenChannel, ChannelOperationError.PeerErrorMessage errorMessage)
        | Ok channelMsg ->
            let theirRevokeAndAckMsg =
                match channelMsg with
                | :? RevokeAndACK as revokeAndAckMsg -> revokeAndAckMsg
                | msg -> raise <| UnexpectedMsg(["RevokeAndAck"], msg)

            let res, channelWrapper =
                let channelCmd = ChannelCommand.ApplyRevokeAndACK theirRevokeAndAckMsg
                channelWrapper.ExecuteCommand channelCmd <| function
                    | (WeAcceptedRevokeAndACK(_))::[] -> Some ()
                    | _ -> None
            let connectedChannel = {
                connectedChannel with
                    PeerWrapper = peerWrapper
                    ChannelWrapper = channelWrapper
            }
            match res with
            | Error err ->
                let! connectedChannel = connectedChannel.SendError (err.ToString())
                let brokenChannel = { BrokenChannel.ConnectedChannel = connectedChannel }
                return Error (brokenChannel, ChannelOperationError.ChannelError err)
            | Ok () ->
                connectedChannel.SaveToWallet()
                let activeChannel = { ConnectedChannel = connectedChannel }
                return Ok activeChannel
    }

    member private this.RecvCommit(): Async<Result<ActiveChannel, BrokenChannel * ChannelOperationError>> = async {
        let connectedChannel = this.ConnectedChannel
        let peerWrapper = connectedChannel.PeerWrapper
        let channelWrapper = connectedChannel.ChannelWrapper

        let! peerWrapper, channelMsgRes = peerWrapper.RecvChannelMsg()
        match channelMsgRes with
        | Error errorMessage ->
            let connectedChannel = {
                connectedChannel with
                    PeerWrapper = peerWrapper
                    ChannelWrapper = channelWrapper
            }
            let brokenChannel = { BrokenChannel.ConnectedChannel = connectedChannel }
            return Error (brokenChannel, ChannelOperationError.PeerErrorMessage errorMessage)
        | Ok channelMsg ->
            let theirCommitmentSignedMsg =
                match channelMsg with
                | :? CommitmentSigned as commitmentSignedMsg -> commitmentSignedMsg
                | msg -> raise <| UnexpectedMsg(["CommitmentSigned"], msg)

            let ourRevokeAndAckMsgRes, channelWrapper =
                let channelCmd = ChannelCommand.ApplyCommitmentSigned theirCommitmentSignedMsg
                channelWrapper.ExecuteCommand channelCmd <| function
                    | (WeAcceptedCommitmentSigned(msg, _))::[] -> Some msg
                    | _ -> None
            match ourRevokeAndAckMsgRes with
            | Error err ->
                let connectedChannel = {
                    connectedChannel with
                        PeerWrapper = peerWrapper
                        ChannelWrapper = channelWrapper
                }
                let! connectedChannel = connectedChannel.SendError (err.ToString())
                let brokenChannel = { BrokenChannel.ConnectedChannel = connectedChannel }
                return Error (brokenChannel, ChannelOperationError.ChannelError err)
            | Ok ourRevokeAndAckMsg ->
                let! peerWrapper = peerWrapper.SendMsg ourRevokeAndAckMsg

                let connectedChannel = {
                    connectedChannel with
                        PeerWrapper = peerWrapper
                        ChannelWrapper = channelWrapper
                }
                connectedChannel.SaveToWallet()
                let activeChannel = { ConnectedChannel = connectedChannel }
                return Ok activeChannel
    }

    member this.SendMonoHopUnidirectionalPayment (amount: LNMoney)
                                                     : Async<Result<ActiveChannel * Result<unit, InvalidMonoHopUnidirectionalPaymentError>, BrokenChannel * ChannelOperationError>> = async {
        let connectedChannel = this.ConnectedChannel
        let peerWrapper = connectedChannel.PeerWrapper
        let channelWrapper = connectedChannel.ChannelWrapper

        let monoHopUnidirectionalPaymentMsgRes, channelWrapper =
            let channelCmd = ChannelCommand.MonoHopUnidirectionalPayment { Amount = amount }
            channelWrapper.ExecuteCommand channelCmd <| function
                | (WeAcceptedCMDMonoHopUnidirectionalPayment(msg, _))::[] -> Some msg
                | _ -> None
        match monoHopUnidirectionalPaymentMsgRes with
        | Error (InvalidMonoHopUnidirectionalPayment err) -> return Ok (this, Error err)
        | Error err -> return failwith <| SPrintF1 "error executing mono hop payment command: %s" (err.ToString())
        | Ok monoHopUnidirectionalPaymentMsg ->
            let! peerWrapper = peerWrapper.SendMsg monoHopUnidirectionalPaymentMsg
            let connectedChannel = {
                connectedChannel with
                    PeerWrapper = peerWrapper
                    ChannelWrapper = channelWrapper
            }
            connectedChannel.SaveToWallet()
            let activeChannel = { ConnectedChannel = connectedChannel }
            let! activeChannelRes = activeChannel.SendCommit()
            match activeChannelRes with
            | Error err -> return Error err
            | Ok activeChannel ->
                let! activeChannelRes = activeChannel.RecvCommit()
                match activeChannelRes with
                | Error err -> return Error err
                | Ok activeChannel -> return Ok (activeChannel, Ok ())
    }

    member this.RecvMonoHopUnidirectionalPayment(): Async<Result<ActiveChannel, BrokenChannel * ChannelOperationError>> = async {
        let connectedChannel = this.ConnectedChannel
        let peerWrapper = connectedChannel.PeerWrapper
        let channelWrapper = connectedChannel.ChannelWrapper

        let! peerWrapper, channelMsgRes = peerWrapper.RecvChannelMsg()
        match channelMsgRes with
        | Error errorMessage ->
            let connectedChannel = {
                connectedChannel with
                    PeerWrapper = peerWrapper
                    ChannelWrapper = channelWrapper
            }
            let brokenChannel = { BrokenChannel.ConnectedChannel = connectedChannel }
            return Error (brokenChannel, ChannelOperationError.PeerErrorMessage errorMessage)
        | Ok channelMsg ->
            let monoHopUnidirectionalPaymentMsg =
                match channelMsg with
                | :? MonoHopUnidirectionalPayment as monoHopUnidirectionalPaymentMsg ->
                    monoHopUnidirectionalPaymentMsg
                | msg -> raise <| UnexpectedMsg(["MonoHopUnidirectionalPayment"], msg)

            let res, channelWrapper =
                let channelCmd =
                    ChannelCommand.ApplyMonoHopUnidirectionalPayment
                        monoHopUnidirectionalPaymentMsg
                channelWrapper.ExecuteCommand channelCmd <| function
                    | (WeAcceptedMonoHopUnidirectionalPayment(_))::[] -> Some ()
                    | _ -> None
            let connectedChannel = {
                connectedChannel with
                    PeerWrapper = peerWrapper
                    ChannelWrapper = channelWrapper
            }
            match res with
            | Error err ->
                let! connectedChannel = connectedChannel.SendError (err.ToString())
                let brokenChannel = { BrokenChannel.ConnectedChannel = connectedChannel }
                return Error (brokenChannel, ChannelOperationError.ChannelError err)
            | Ok () ->
                connectedChannel.SaveToWallet()
                let activeChannel = { ConnectedChannel = connectedChannel }
                let! activeChannelRes = activeChannel.RecvCommit()
                match activeChannelRes with
                | Error err -> return Error err
                | Ok activeChannel ->
                    return! activeChannel.SendCommit()
    }

