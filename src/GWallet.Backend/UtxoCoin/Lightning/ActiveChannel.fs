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

type LockFundingError =
    | RecvFundingLocked of RecvMsgError
    | FundingLockedPeerErrorResponse of BrokenChannel * PeerErrorMessage
    | ExpectedFundingLocked of ILightningMsg
    | InvalidFundingLocked of BrokenChannel * ChannelError
    with
    member this.Message =
        match this with
        | RecvFundingLocked err ->
            SPrintF1 "Error receiving funding locked: %s" err.Message
        | FundingLockedPeerErrorResponse (_, err) ->
            SPrintF1 "Peer responded to our funding_locked with an error: %s" err.Message
        | ExpectedFundingLocked msg ->
            SPrintF1 "Expected funding_locked message, got %A" (msg.GetType())
        | InvalidFundingLocked (_, err) ->
            SPrintF1 "Invalid funding_locked message: %s" err.Message
    member this.PossibleBug =
        match this with
        | RecvFundingLocked err -> err.PossibleBug
        | FundingLockedPeerErrorResponse _
        | ExpectedFundingLocked _
        | InvalidFundingLocked _ -> false

type ReconnectActiveChannelError =
    | Reconnect of ReconnectError
    | LockFunding of LockFundingError
    with
    member this.Message =
        match this with
        | Reconnect err ->
            SPrintF1 "Error reconnecting: %s" err.Message
        | LockFunding err ->
            SPrintF1 "Error locking funding: %s" err.Message
    member this.PossibleBug =
        match this with
        | Reconnect err -> err.PossibleBug
        | LockFunding err -> err.PossibleBug

type SendCommitError =
    | RecvRevokeAndAck of RecvMsgError
    | CommitmentSignedPeerErrorResponse of BrokenChannel * PeerErrorMessage
    | ExpectedRevokeAndAck of ILightningMsg
    | InvalidRevokeAndAck of BrokenChannel * ChannelError
    with
    member this.Message =
        match this with
        | RecvRevokeAndAck err ->
            SPrintF1 "Error receiving revoke_and_ack: %s" err.Message
        | CommitmentSignedPeerErrorResponse (_, err) ->
            SPrintF1 "Peer responded to our commitment_signed with an error message: %s" err.Message
        | ExpectedRevokeAndAck msg ->
            SPrintF1 "Expected revoke_and_ack, got %A" (msg.GetType())
        | InvalidRevokeAndAck (_, err) ->
            SPrintF1 "Invalid revoke_and_ack: %s" err.Message
    member this.PossibleBug =
        match this with
        | RecvRevokeAndAck err -> err.PossibleBug
        | CommitmentSignedPeerErrorResponse _
        | ExpectedRevokeAndAck _
        | InvalidRevokeAndAck _ -> false

type RecvCommitError =
    | RecvCommitmentSigned of RecvMsgError
    | PeerErrorMessageInsteadOfCommitmentSigned of BrokenChannel * PeerErrorMessage
    | ExpectedCommitmentSigned of ILightningMsg
    | InvalidCommitmentSigned of BrokenChannel * ChannelError
    with
    member this.Message =
        match this with
        | RecvCommitmentSigned err ->
            SPrintF1 "Error receiving commitment_signed: %s" err.Message
        | PeerErrorMessageInsteadOfCommitmentSigned (_, err) ->
            SPrintF1 "Peer sent us an error message instead of commitment_signed: %s" err.Message
        | ExpectedCommitmentSigned msg ->
            SPrintF1 "Expected commitment_signed, got %A" (msg.GetType())
        | InvalidCommitmentSigned (_, err) ->
            SPrintF1 "Invalid commitment signed: %s" err.Message
    member this.PossibleBug =
        match this with
        | RecvCommitmentSigned err -> err.PossibleBug
        | PeerErrorMessageInsteadOfCommitmentSigned _
        | ExpectedCommitmentSigned _
        | InvalidCommitmentSigned _ -> false

type SendMonoHopPaymentError =
    | InvalidMonoHopPayment of ActiveChannel * InvalidMonoHopUnidirectionalPaymentError
    | SendCommit of SendCommitError
    | RecvCommit of RecvCommitError
    with
    member this.Message =
        match this with
        | InvalidMonoHopPayment (_, err) ->
            SPrintF1 "Invalid monohop payment: %s" err.Message
        | SendCommit err ->
            SPrintF1 "Error sending commitment: %s" err.Message
        | RecvCommit err ->
            SPrintF1 "Error receiving commitment: %s" err.Message
    member this.PossibleBug =
        match this with
        | InvalidMonoHopPayment _ -> false
        | SendCommit err -> err.PossibleBug
        | RecvCommit err -> err.PossibleBug

and RecvMonoHopPaymentError =
    | RecvMonoHopPayment of RecvMsgError
    | PeerErrorMessageInsteadOfMonoHopPayment of BrokenChannel * PeerErrorMessage
    | InvalidMonoHopPayment of BrokenChannel * ChannelError
    | ExpectedMonoHopPayment of ILightningMsg
    | RecvCommit of RecvCommitError
    | SendCommit of SendCommitError
    with
    member this.Message =
        match this with
        | RecvMonoHopPayment err ->
            SPrintF1 "Error receiving monohop payment message: %s" err.Message
        | PeerErrorMessageInsteadOfMonoHopPayment (_, err) ->
            SPrintF1 "Peer sent us an error message instead of a monohop payment: %s" err.Message
        | InvalidMonoHopPayment (_, err) ->
            SPrintF1 "Invalid monohop payment message: %s" err.Message
        | ExpectedMonoHopPayment msg ->
            SPrintF1 "Expected monohop payment msg, got %A" (msg.GetType())
        | RecvCommit err ->
            SPrintF1 "Error receiving commitment: %s" err.Message
        | SendCommit err ->
            SPrintF1 "Error sending commitment: %s" err.Message
    member this.PossibleBug =
        match this with
        | RecvMonoHopPayment err -> err.PossibleBug
        | RecvCommit err -> err.PossibleBug
        | SendCommit err -> err.PossibleBug
        | PeerErrorMessageInsteadOfMonoHopPayment _
        | InvalidMonoHopPayment _
        | ExpectedMonoHopPayment _ -> false

and ActiveChannel = {
    ConnectedChannel: ConnectedChannel
} with
    interface IDisposable with
        member this.Dispose() =
            (this.ConnectedChannel :> IDisposable).Dispose()

    static member private LockFunding (fundedChannel: FundedChannel)
                                      (confirmationCount: BlockHeightOffset32)
                                          : Async<Result<ActiveChannel, LockFundingError>> = async {
        let theirFundingLockedMsgOpt = fundedChannel.TheirFundingLockedMsgOpt
        if confirmationCount < fundedChannel.ConnectedChannel.MinimumDepth then
            failwith
                "LockFunding called when required confirmation depth has not been reached"
        let! absoluteBlockHeight, txIndex = fundedChannel.GetLocationOnChain()
        let connectedChannel = fundedChannel.ConnectedChannel
        let peerWrapper = connectedChannel.PeerWrapper
        let channelWrapper = connectedChannel.ChannelWrapper
        let ourFundingLockedMsgRes, channelWrapperAfterFundingConfirmed =
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
        let ourFundingLockedMsg = Unwrap ourFundingLockedMsgRes "DNL error creating funding_locked msg"
        let! peerWrapperAfterFundingLockedSent = peerWrapper.SendMsg ourFundingLockedMsg
        let! theirFundingLockedMsgRes = async {
            match theirFundingLockedMsgOpt with
            | Some theirFundingLockedMsg -> return Ok (peerWrapperAfterFundingLockedSent, theirFundingLockedMsg)
            | None ->
                let! recvChannelMsgRes = peerWrapperAfterFundingLockedSent.RecvChannelMsg()
                match recvChannelMsgRes with
                | Error (RecvMsg recvMsgError) -> return Error <| RecvFundingLocked recvMsgError
                | Error (ReceivedPeerErrorMessage (peerWrapperAfterFundingLockedReceived, errorMessage)) ->
                    let connectedChannelAfterError = {
                        connectedChannel with
                            PeerWrapper = peerWrapperAfterFundingLockedReceived
                            ChannelWrapper = channelWrapperAfterFundingConfirmed
                    }
                    let brokenChannel = { BrokenChannel.ConnectedChannel = connectedChannelAfterError }
                    return Error <| FundingLockedPeerErrorResponse
                        (brokenChannel, errorMessage)
                | Ok (peerWrapperAfterFundingLockedReceived, channelMsg) ->
                    match channelMsg with
                    | :? FundingLockedMsg as fundingLockedMsg ->
                        return Ok (peerWrapperAfterFundingLockedReceived, fundingLockedMsg)
                    | _ -> return Error <| ExpectedFundingLocked channelMsg
        }

        match theirFundingLockedMsgRes with
        | Error err -> return Error err
        | Ok (peerWrapperAfterFundingLockedReceived, theirFundingLockedMsg) ->
            let res, channelWrapperAfterFundingLocked =
                let channelCmd = ApplyFundingLocked theirFundingLockedMsg
                channelWrapperAfterFundingConfirmed.ExecuteCommand channelCmd <| function
                    | (BothFundingLocked _)::[] -> Some ()
                    | _ -> None
            let connectedChannelAfterFundingLocked = {
                connectedChannel with
                    PeerWrapper = peerWrapperAfterFundingLockedReceived
                    ChannelWrapper = channelWrapperAfterFundingLocked
            }
            match res with
            | Error err ->
                let! connectedChannelAfterErrorSent = connectedChannelAfterFundingLocked.SendError err.Message
                let brokenChannel = { BrokenChannel.ConnectedChannel = connectedChannelAfterErrorSent }
                return Error <| InvalidFundingLocked
                    (brokenChannel, err)
            | Ok () ->
                connectedChannelAfterFundingLocked.SaveToWallet()
                let activeChannel: ActiveChannel = {
                    ConnectedChannel = connectedChannelAfterFundingLocked
                }
                return Ok activeChannel
    }

    static member private WaitForFundingLocked (fundedChannel: FundedChannel)
                                                   : Async<Result<ActiveChannel, LockFundingError>> = async {
        let rec waitForRequiredConfirmations() = async {
            let! confirmationCount = fundedChannel.GetConfirmations()
            if confirmationCount < fundedChannel.MinimumDepth then
                let remainingConfirmations = fundedChannel.MinimumDepth - confirmationCount
                Console.WriteLine(SPrintF1 "Waiting for %i more confirmations." remainingConfirmations.Value)
                let sleepTime =
                    int(TimeSpan.FromMinutes(5.0).TotalMilliseconds) * int remainingConfirmations.Value
                do! Async.Sleep sleepTime
                return! waitForRequiredConfirmations()
            else
                return confirmationCount
        }
        let! confirmationCount = waitForRequiredConfirmations()
        return! ActiveChannel.LockFunding fundedChannel confirmationCount
    }

    static member private ConfirmFundingLocked (connectedChannel: ConnectedChannel)
                                                   : Async<Result<ActiveChannel, LockFundingError>> = async {
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

    static member ConnectReestablish (nodeSecret: ExtKey)
                                     (channelId: ChannelId)
                                         : Async<Result<ActiveChannel, ReconnectActiveChannelError>> = async {
        let! connectRes =
            ConnectedChannel.ConnectFromWallet nodeSecret channelId
        match connectRes with
        | Error reconnectError -> return Error <| Reconnect reconnectError
        | Ok connectedChannel ->
            let! activeChannelRes = ActiveChannel.ConfirmFundingLocked connectedChannel
            match activeChannelRes with
            | Error lockFundingError -> return Error <| LockFunding lockFundingError
            | Ok activeChannel -> return Ok activeChannel
    }

    static member AcceptReestablish (transportListener: TransportListener)
                                    (channelId: ChannelId)
                                        : Async<Result<ActiveChannel, ReconnectActiveChannelError >> = async {
        let! connectRes =
            ConnectedChannel.AcceptFromWallet transportListener channelId
        match connectRes with
        | Error reconnectError -> return Error <| Reconnect reconnectError
        | Ok connectedChannel ->
            let! activeChannelRes = ActiveChannel.ConfirmFundingLocked connectedChannel
            match activeChannelRes with
            | Error lockFundingError -> return Error <| LockFunding lockFundingError
            | Ok activeChannel -> return Ok activeChannel
    }

    member this.Balance
        with get(): LNMoney =
            UnwrapOption
                (this.ConnectedChannel.ChannelWrapper.Balance())
                "The ActiveChannel type is created by establishing a channel \
                and so guarantees that the underlying channel state has a balance"

    member this.SpendableBalance
        with get(): LNMoney =
            UnwrapOption
                (this.ConnectedChannel.ChannelWrapper.SpendableBalance())
                "The ActiveChannel type is created by establishing a channel \
                and so guarantees that the underlying channel state has a balance"

    member this.ChannelId
        with get(): ChannelId = this.ConnectedChannel.ChannelId

    member private this.SendCommit(): Async<Result<ActiveChannel, SendCommitError>> = async {
        let connectedChannel = this.ConnectedChannel
        let peerWrapper = connectedChannel.PeerWrapper
        let channelWrapper = connectedChannel.ChannelWrapper

        let ourCommitmentSignedMsgRes, channelWrapperAfterCommitmentSigned =
            let channelCmd = ChannelCommand.SignCommitment
            channelWrapper.ExecuteCommand channelCmd <| function
                | (WeAcceptedOperationSign(msg, _))::[] -> Some msg
                | _ -> None
        let ourCommitmentSignedMsg = Unwrap ourCommitmentSignedMsgRes "error executing sign commit command"

        let! peerWrapperAfterCommitmentSignedSent = peerWrapper.SendMsg ourCommitmentSignedMsg

        let! recvChannelMsgRes = peerWrapperAfterCommitmentSignedSent.RecvChannelMsg()
        match recvChannelMsgRes with
        | Error (RecvMsg recvMsgError) -> return Error <| RecvRevokeAndAck recvMsgError
        | Error (ReceivedPeerErrorMessage (peerWrapperAfterRevokeAndAckReceived, errorMessage)) ->
            let connectedChannelAfterError = {
                connectedChannel with
                    PeerWrapper = peerWrapperAfterRevokeAndAckReceived
                    ChannelWrapper = channelWrapperAfterCommitmentSigned
            }
            let brokenChannel = { BrokenChannel.ConnectedChannel = connectedChannelAfterError }
            return Error <| CommitmentSignedPeerErrorResponse
                (brokenChannel, errorMessage)
        | Ok (peerWrapperAfterRevokeAndAckReceived, channelMsg) ->
            match channelMsg with
            | :? RevokeAndACKMsg as theirRevokeAndAckMsg ->
                let res, channelWrapperAferRevokeAndAck =
                    let channelCmd = ChannelCommand.ApplyRevokeAndACK theirRevokeAndAckMsg
                    channelWrapperAfterCommitmentSigned.ExecuteCommand channelCmd <| function
                        | (WeAcceptedRevokeAndACK(_))::[] -> Some ()
                        | _ -> None
                let connectedChannelAfterRevokeAndAck = {
                    connectedChannel with
                        PeerWrapper = peerWrapperAfterRevokeAndAckReceived
                        ChannelWrapper = channelWrapperAferRevokeAndAck
                }
                match res with
                | Error err ->
                    let! connectedChannelAfterErrorSent = connectedChannelAfterRevokeAndAck.SendError err.Message
                    let brokenChannel = { BrokenChannel.ConnectedChannel = connectedChannelAfterErrorSent }
                    return Error <| InvalidRevokeAndAck
                        (brokenChannel, err)
                | Ok () ->
                    connectedChannelAfterRevokeAndAck.SaveToWallet()
                    let activeChannel = { ConnectedChannel = connectedChannelAfterRevokeAndAck }
                    return Ok activeChannel
            | _ -> return Error <| ExpectedRevokeAndAck channelMsg
    }

    member private this.RecvCommit(): Async<Result<ActiveChannel, RecvCommitError>> = async {
        let connectedChannel = this.ConnectedChannel
        let peerWrapper = connectedChannel.PeerWrapper
        let channelWrapper = connectedChannel.ChannelWrapper

        let! recvChannelMsgRes = peerWrapper.RecvChannelMsg()
        match recvChannelMsgRes with
        | Error (RecvMsg recvMsgError) -> return Error <| RecvCommitmentSigned recvMsgError
        | Error (ReceivedPeerErrorMessage (peerWrapperAfterCommitmentSignedReceived, errorMessage)) ->
            let connectedChannelAfterError = {
                connectedChannel with
                    PeerWrapper = peerWrapperAfterCommitmentSignedReceived
                    ChannelWrapper = channelWrapper
            }
            let brokenChannel = { BrokenChannel.ConnectedChannel = connectedChannelAfterError }
            return Error <| PeerErrorMessageInsteadOfCommitmentSigned
                (brokenChannel, errorMessage)
        | Ok (peerWrapperAfterCommitmentSignedReceived, channelMsg) ->
            match channelMsg with
            | :? CommitmentSignedMsg as theirCommitmentSignedMsg ->
                let ourRevokeAndAckMsgRes, channelWrapperAfterCommitmentSigned =
                    let channelCmd = ChannelCommand.ApplyCommitmentSigned theirCommitmentSignedMsg
                    channelWrapper.ExecuteCommand channelCmd <| function
                        | (WeAcceptedCommitmentSigned(msg, _))::[] -> Some msg
                        | _ -> None
                match ourRevokeAndAckMsgRes with
                | Error err ->
                    let connectedChannelAfterError = {
                        connectedChannel with
                            PeerWrapper = peerWrapperAfterCommitmentSignedReceived
                            ChannelWrapper = channelWrapperAfterCommitmentSigned
                    }
                    let! connectedChannelAfterErrorSent = connectedChannelAfterError.SendError err.Message
                    let brokenChannel = { BrokenChannel.ConnectedChannel = connectedChannelAfterErrorSent }
                    return Error <| InvalidCommitmentSigned
                        (brokenChannel, err)
                | Ok ourRevokeAndAckMsg ->
                    let! peerWrapperAfterRevokeAndAckSent = peerWrapperAfterCommitmentSignedReceived.SendMsg ourRevokeAndAckMsg

                    let connectedChannelAfterRevokeAndAck = {
                        connectedChannel with
                            PeerWrapper = peerWrapperAfterRevokeAndAckSent
                            ChannelWrapper = channelWrapperAfterCommitmentSigned
                    }
                    connectedChannelAfterRevokeAndAck.SaveToWallet()
                    let activeChannel = { ConnectedChannel = connectedChannelAfterRevokeAndAck }
                    return Ok activeChannel
            | _ -> return Error <| ExpectedCommitmentSigned channelMsg
    }

    member this.SendMonoHopUnidirectionalPayment (amount: LNMoney)
                                                     : Async<Result<ActiveChannel, SendMonoHopPaymentError>> = async {
        let connectedChannel = this.ConnectedChannel
        let peerWrapper = connectedChannel.PeerWrapper
        let channelWrapper = connectedChannel.ChannelWrapper

        let monoHopUnidirectionalPaymentMsgRes, channelWrapperAfterMonoHopPayment =
            let channelCmd = ChannelCommand.MonoHopUnidirectionalPayment { Amount = amount }
            channelWrapper.ExecuteCommand channelCmd <| function
                | (WeAcceptedOperationMonoHopUnidirectionalPayment(msg, _))::[] -> Some msg
                | _ -> None
        match monoHopUnidirectionalPaymentMsgRes with
        | Error (InvalidMonoHopUnidirectionalPayment err) ->
            return Error <| SendMonoHopPaymentError.InvalidMonoHopPayment (this, err)
        | Error err -> return failwith <| SPrintF1 "error executing mono hop payment command: %s" err.Message
        | Ok monoHopUnidirectionalPaymentMsg ->
            let! peerWrapperAfterMonoHopPaymentSent = peerWrapper.SendMsg monoHopUnidirectionalPaymentMsg
            let connectedChannelAfterMonoHopPaymentSent = {
                connectedChannel with
                    PeerWrapper = peerWrapperAfterMonoHopPaymentSent
                    ChannelWrapper = channelWrapperAfterMonoHopPayment
            }
            connectedChannelAfterMonoHopPaymentSent.SaveToWallet()
            let activeChannel = { ConnectedChannel = connectedChannelAfterMonoHopPaymentSent }
            let! activeChannelAfterCommitSentRes = activeChannel.SendCommit()
            match activeChannelAfterCommitSentRes with
            | Error err -> return Error <| SendMonoHopPaymentError.SendCommit err
            | Ok activeChannelAfterCommitSent ->
                let! activeChannelAfterCommitReceivedRes = activeChannelAfterCommitSent.RecvCommit()
                match activeChannelAfterCommitReceivedRes with
                | Error err -> return Error <| SendMonoHopPaymentError.RecvCommit err
                | Ok activeChannelAfterCommitReceived -> return Ok activeChannelAfterCommitReceived
    }

    member this.RecvMonoHopUnidirectionalPayment (monoHopUnidirectionalPaymentMsg: MonoHopUnidirectionalPaymentMsg): Async<Result<ActiveChannel, RecvMonoHopPaymentError>> = async {
        let connectedChannel = this.ConnectedChannel
        let channelWrapper = connectedChannel.ChannelWrapper

        let res, channelWrapperAfterMonoHopPaymentReceived =
            let channelCmd =
                ChannelCommand.ApplyMonoHopUnidirectionalPayment
                    monoHopUnidirectionalPaymentMsg
            channelWrapper.ExecuteCommand channelCmd <| function
                | (WeAcceptedMonoHopUnidirectionalPayment(_))::[] -> Some ()
                | _ -> None
        let connectedChannelAfterMonoHopPaymentReceived = {
            connectedChannel with
                ChannelWrapper = channelWrapperAfterMonoHopPaymentReceived
        }
        match res with
        | Error err ->
            let! connectedChannelAfterError = connectedChannelAfterMonoHopPaymentReceived.SendError err.Message
            let brokenChannel = { BrokenChannel.ConnectedChannel = connectedChannelAfterError }
            return Error <| InvalidMonoHopPayment
                (brokenChannel, err)
        | Ok () ->
            connectedChannelAfterMonoHopPaymentReceived.SaveToWallet()
            let activeChannel = { ConnectedChannel = connectedChannelAfterMonoHopPaymentReceived }
            let! activeChannelAfterCommitReceivedRes = activeChannel.RecvCommit()
            match activeChannelAfterCommitReceivedRes with
            | Error err -> return Error <| RecvMonoHopPaymentError.RecvCommit err
            | Ok activeChannelAfterCommitReceived ->
                let! activeChannelAfterCommitSentRes = activeChannelAfterCommitReceived.SendCommit()
                match activeChannelAfterCommitSentRes with
                | Error err -> return Error <| RecvMonoHopPaymentError.SendCommit err
                | Ok activeChannelAfterCommitSent -> return Ok activeChannelAfterCommitSent

    }

