namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Linq

open NBitcoin

open DotNetLightning.Utils
open DotNetLightning.Serialize.Msgs
open DotNetLightning.Channel

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks
open GWallet.Backend.UtxoCoin
open GWallet.Backend.UtxoCoin.Lightning.Util

open FSharp.Core

type CloseChannelError =
    | OperationCloseFailed of string
    | CloseCommandFailed of ChannelError
    | RecvFailed of RecvMsgError
    | RecvPeerError of BrokenChannel * PeerErrorMessage
    | ExpectedShutdownMsg of ILightningMsg
    | RemoteShutdownCommandFailed of BrokenChannel * ChannelError
    | ExpectedClosingSignedMsg of ILightningMsg
    | ApplyClosingSignedFailed of ChannelError
    | UnexpectedApplyClosingSignedResult
    member self.Message =
        match self with
        | OperationCloseFailed err -> SPrintF1 "Failed to create close operation: %s" err
        | CloseCommandFailed err -> SPrintF1 "Failed to apply close command to the channel: %s" err.Message
        | RecvFailed err -> SPrintF1 "Failed to receive response from peer: %s" err.Message
        | RecvPeerError (_, err) -> SPrintF1 "Peer responded with an error: %s" err.Message
        | ExpectedShutdownMsg msg ->
            SPrintF2 "Expected to receive a Shutdown message from peer, but got %A: %s" msg (msg.ToString())
        | RemoteShutdownCommandFailed (_, err) ->
            SPrintF1 "Failed to apply RemoteShutdown command to the channel: %s" err.Message
        | ExpectedClosingSignedMsg msg ->
            SPrintF2 "Expected to receive a ClosingSigned message from peer, but got %A: %s" msg (msg.ToString())
        | ApplyClosingSignedFailed err ->
            SPrintF1 "Failed to apply ClosingSigned command to the channel: %s" err.Message
        | UnexpectedApplyClosingSignedResult ->
            "Expected to either receive a MutualClosePerformed or next ClosingSigned"


(*
    +-------+                              +-------+
    |       |--(1)-----  shutdown  ------->|       |
    |       |<-(2)-----  shutdown  --------|       |
    |       |                              |       |
    |       | <complete all pending HTLCs> |       |
    |   A   |                 ...          |   B   |
    |       |                              |       |
    |       |--(3)-- closing_signed  F1--->|       |
    |       |<-(4)-- closing_signed  F2----|       |
    |       |              ...             |       |
    |       |--(?)-- closing_signed  Fn--->|       |
    |       |<-(?)-- closing_signed  Fn----|       |
    +-------+                              +-------+
*)

type ClosedChannel =
    { ActiveChannel: ActiveChannel
      OurPayoutScript: Script }
    interface IDisposable with
        member self.Dispose() =
            (self.ActiveChannel :> IDisposable).Dispose()

    static member InitiateCloseChannel(activeChannel: ActiveChannel): Async<Result<ClosedChannel, CloseChannelError>> =
        async {
            let ourPayoutScript =
                activeChannel.ConnectedChannel.ChannelWrapper.Channel.Config.ChannelOptions.ShutdownScriptPubKey.Value

            let initialChannel =
                { ActiveChannel = activeChannel
                  OurPayoutScript = ourPayoutScript }

            let! shutdownSendResult = initialChannel.initiateShutdown ()

            match shutdownSendResult with
            | Error e -> return Error <| e
            | Ok closedChannelAfterShutdownSent ->
                let! closingSignedExchangeResult = closedChannelAfterShutdownSent.runClosingSignedExchange

                match closingSignedExchangeResult with
                | Error e -> return Error <| e
                | Ok closedChannelAfterClosingSignedExchange -> return Ok closedChannelAfterClosingSignedExchange
        }

    static member AwaitCloseChannel(activeChannel: ActiveChannel): Async<Result<ClosedChannel, CloseChannelError>> =
        async {
            let ourPayoutScript =
                activeChannel.ConnectedChannel.ChannelWrapper.Channel.Config.ChannelOptions.ShutdownScriptPubKey.Value

            let initialChannel =
                { ActiveChannel = activeChannel
                  OurPayoutScript = ourPayoutScript }

            let! shutdownReceiveResult = initialChannel.receiveShutdown ()

            match shutdownReceiveResult with
            | Error e -> return Error <| e
            | Ok closedChannelAfterShutdownSent ->
                let! closingSignedExchangeResult = closedChannelAfterShutdownSent.runClosingSignedExchange

                match closingSignedExchangeResult with
                | Error e -> return Error <| e
                | Ok closedChannelAfterClosingSignedExchange -> return Ok closedChannelAfterClosingSignedExchange
        }

    member private self.initiateShutdown(): Async<Result<ClosedChannel, CloseChannelError>> =
        async {
            Infrastructure.LogDebug "Sending shutdown message"
            let connectedChannel = self.ActiveChannel.ConnectedChannel
            let channelWrapper = connectedChannel.ChannelWrapper
            let peerWrapper = connectedChannel.PeerWrapper
            match OperationClose.Create self.OurPayoutScript with
            | Error e -> return Error <| OperationCloseFailed e
            | Ok op ->
                let channelCommand = ChannelCommand.Close op

                let closeChannelMsgRes, channelWrapperAfterCloseChannel =
                    channelWrapper.ExecuteCommand channelCommand
                    <| function
                    | (AcceptedOperationShutdown (closeChannelMsg) :: []) -> Some closeChannelMsg
                    | _ -> None

                match closeChannelMsgRes with
                | Error channelError -> return Error <| (CloseCommandFailed channelError)
                | Ok closeChannelMsg ->
                    let! peerWrapperAfterCloseChannel = peerWrapper.SendMsg closeChannelMsg
                    Infrastructure.LogDebug "Waiting for response to shutdown message"
                    let! recvChannelMsgRes = peerWrapperAfterCloseChannel.RecvChannelMsg()

                    match recvChannelMsgRes with
                    | Error (RecvMsg recvMsgError) -> return Error <| RecvFailed recvMsgError
                    | Error (ReceivedPeerErrorMessage (peerWrapperAfterShutdownChannel, errorMessage)) ->
                        let connectedChannelAfterError =
                            { connectedChannel with
                                  ChannelWrapper = channelWrapperAfterCloseChannel
                                  PeerWrapper = peerWrapperAfterShutdownChannel }

                        let brokenChannel =
                            { BrokenChannel.ConnectedChannel = connectedChannelAfterError }

                        return Error
                               <| RecvPeerError(brokenChannel, errorMessage)
                    | Ok (peerWrapperAfterShutdownChannelReceived, channelMsg) ->
                        let closedChannelAfterShutdownChannelReceived =
                            { self with
                                  ActiveChannel =
                                      { ConnectedChannel =
                                            { self.ActiveChannel.ConnectedChannel with
                                                  ChannelWrapper = channelWrapperAfterCloseChannel
                                                  PeerWrapper = peerWrapperAfterShutdownChannelReceived } } }

                        match channelMsg with
                        | :? ShutdownMsg as shutdownMsg ->
                            return! (closedChannelAfterShutdownChannelReceived.handleRemoteShutdown shutdownMsg true)
                        | _ -> return Error <| ExpectedShutdownMsg channelMsg
        }

    member private self.receiveShutdown(): Async<Result<ClosedChannel, CloseChannelError>> =
        async {
            Infrastructure.LogDebug "Waiting for shutdown message"
            let connectedChannel = self.ActiveChannel.ConnectedChannel
            let! recvChannelMsgRes = connectedChannel.PeerWrapper.RecvChannelMsg()

            match recvChannelMsgRes with
            | Error (RecvMsg recvMsgError) -> return Error <| RecvFailed recvMsgError
            | Error (ReceivedPeerErrorMessage (peerWrapperAfterShutdownChannel, errorMessage)) ->
                let connectedChannelAfterError =
                    { connectedChannel with
                          PeerWrapper = peerWrapperAfterShutdownChannel }

                let brokenChannel =
                    { BrokenChannel.ConnectedChannel = connectedChannelAfterError }

                return Error
                       <| RecvPeerError(brokenChannel, errorMessage)
            | Ok (peerWrapperAfterShutdownChannelReceived, channelMsg) ->
                let closedChannelAfterShutdownChannelReceived =
                    { self with
                          ActiveChannel =
                              { ConnectedChannel =
                                    { self.ActiveChannel.ConnectedChannel with
                                          PeerWrapper = peerWrapperAfterShutdownChannelReceived } } }

                match channelMsg with
                | :? ShutdownMsg as shutdownMsg ->
                    return! (closedChannelAfterShutdownChannelReceived.handleRemoteShutdown shutdownMsg false)
                | _ -> return Error <| ExpectedShutdownMsg channelMsg
        }

    member private self.handleRemoteShutdown shutdownMsg sentOurs: Async<Result<ClosedChannel, CloseChannelError>> =
        async {
            Infrastructure.LogDebug "Received remote shutdown message"
            let connectedChannel = self.ActiveChannel.ConnectedChannel

            let peerWrapperAfterShutdownChannelReceived =
                self.ActiveChannel.ConnectedChannel.PeerWrapper

            let remoteShutdownCmd =
                ChannelCommand.RemoteShutdown shutdownMsg

            let shutdownResponseResult, channelWrapperAfterShutdownResponse =
                connectedChannel.ChannelWrapper.ExecuteCommand remoteShutdownCmd
                <| function
                | (AcceptedShutdownWhenNoPendingHTLCs (msg, nextState)) :: [] -> Some(msg, nextState)
                | _ -> None

            match shutdownResponseResult with
            | Error err ->
                let connectedChannelAfterError =
                    { connectedChannel with
                          ChannelWrapper = channelWrapperAfterShutdownResponse }

                let! connectedChannelAfterErrorSent = connectedChannelAfterError.SendError err.Message

                let brokenChannel =
                    { BrokenChannel.ConnectedChannel = connectedChannelAfterErrorSent }

                return Error
                       <| RemoteShutdownCommandFailed(brokenChannel, err)
            | Ok shutdownResponseMsg ->
                match shutdownResponseMsg with
                // Depending on if we are the funder or not we have to send the first ClosingSigned
                | closingSignedOpt, negotiationData ->

                    let mutable peerWrapperAfterShutdownResponse = peerWrapperAfterShutdownChannelReceived
                    if not sentOurs then
                        Infrastructure.LogDebug "Responding with our shutdown"
                        let! p = peerWrapperAfterShutdownChannelReceived.SendMsg negotiationData.LocalShutdown
                        peerWrapperAfterShutdownResponse <- p

                    match closingSignedOpt with
                    | Some closingSigned ->
                        Infrastructure.LogDebug "We are funder, sending initial ClosingSigned"
                        Infrastructure.LogDebug(SPrintF1 "ClosingSigned: %A" closingSigned)
                        let! peerWrapperAfterClosingSigned =
                            peerWrapperAfterShutdownChannelReceived.SendMsg closingSigned

                        return Ok
                                   { self with
                                         ActiveChannel =
                                             { ConnectedChannel =
                                                   { self.ActiveChannel.ConnectedChannel with
                                                         PeerWrapper = peerWrapperAfterClosingSigned
                                                         ChannelWrapper = channelWrapperAfterShutdownResponse } } }
                    | None ->
                        Infrastructure.LogDebug "We are not funder, waiting for initial ClosingSigned from peer"

                        return Ok
                                   { self with
                                         ActiveChannel =
                                             { ConnectedChannel =
                                                   { self.ActiveChannel.ConnectedChannel with
                                                         PeerWrapper = peerWrapperAfterShutdownResponse
                                                         ChannelWrapper = channelWrapperAfterShutdownResponse } } }
        }

    member private self.runClosingSignedExchange : Async<Result<ClosedChannel, CloseChannelError>> =
        async {
            Infrastructure.LogDebug "Starting closingSigned exchange loop"

            let rec exchange channel =
                async {
                    Infrastructure.LogDebug "Waiting for next closingSigned message from peer"
                    let connectedChannel = channel.ActiveChannel.ConnectedChannel
                    let! recvChannelMsgRes = connectedChannel.PeerWrapper.RecvChannelMsg()

                    match recvChannelMsgRes with
                    | Error (RecvMsg _) ->
                        // We assume the peer closed the connection after broadcasting the tx
                        Infrastructure.LogDebug "Peer closed connection after successful closing negotiation"
                        self.ActiveChannel.ConnectedChannel.SaveToWallet()
                        return Ok self
                    | Error (ReceivedPeerErrorMessage (peerWrapperAfterClosingSignedReceived, errorMessage)) ->
                        let connectedChannelAfterError =
                            { connectedChannel with
                                  PeerWrapper = peerWrapperAfterClosingSignedReceived }

                        let brokenChannel =
                            { BrokenChannel.ConnectedChannel = connectedChannelAfterError }

                        return Error
                               <| RecvPeerError(brokenChannel, errorMessage)
                    | Ok (peerWrapperAfterClosingSignedReceived, channelMsg) ->
                        let connectedChannelAfterClosingSignedReceived =
                            { self.ActiveChannel.ConnectedChannel with
                                  PeerWrapper = peerWrapperAfterClosingSignedReceived }

                        let closedChannelAfterClosingSignedReceived =
                            { self with
                                  ActiveChannel = { ConnectedChannel = connectedChannelAfterClosingSignedReceived } }

                        match channelMsg with
                        | :? ClosingSignedMsg as closingSignedMsg ->
                            Infrastructure.LogDebug "Received closingSigned from peer"
                            Infrastructure.LogDebug(SPrintF1 "ClosingSigned: %A" closingSignedMsg)

                            let closingSignedCmd =
                                ChannelCommand.ApplyClosingSigned closingSignedMsg

                            let closingSignedResponseResult, channelWrapperAfterClosingSignedResponse =
                                connectedChannelAfterClosingSignedReceived.ChannelWrapper.ExecuteCommand
                                    closingSignedCmd
                                <| function
                                | (WeProposedNewClosingSigned (msg, _)) :: [] -> Some(Some msg, None)
                                | (MutualClosePerformed (finalizedTx, _)) :: [] -> Some(None, Some finalizedTx)
                                | _ -> None

                            match closingSignedResponseResult with
                            | Ok closingSignedResponseMsg ->
                                match closingSignedResponseMsg with
                                | (Some msg, None) ->
                                    Infrastructure.LogDebug "Responding with new closingSigned"
                                    Infrastructure.LogDebug(SPrintF1 "ClosingSigned: %A" msg)
                                    let! peerWrapperAfterClosingSignedResponse =
                                        peerWrapperAfterClosingSignedReceived.SendMsg msg

                                    let closedChannelAfterClosingSignedResponse =
                                        { closedChannelAfterClosingSignedReceived with
                                              ActiveChannel =
                                                  { ConnectedChannel =
                                                        { connectedChannelAfterClosingSignedReceived with
                                                              PeerWrapper = peerWrapperAfterClosingSignedResponse
                                                              ChannelWrapper = channelWrapperAfterClosingSignedResponse } } }

                                    let! result = exchange closedChannelAfterClosingSignedResponse

                                    return result
                                | (None, Some finalizedTx) ->
                                    Infrastructure.LogDebug "Mutual close performed"
                                    Infrastructure.LogDebug(SPrintF1 "FinalizedTX: %A" finalizedTx)
                                    Infrastructure.LogDebug(SPrintF1 "ourPayoutScript: %A" self.OurPayoutScript)

                                    let! _txid =
                                        let signedTx: string = finalizedTx.Value.ToHex()
                                        Infrastructure.LogDebug(SPrintF1 "Broadcasting tx: %A" signedTx)
                                        Account.BroadcastRawTransaction Currency.BTC signedTx

                                    Infrastructure.LogDebug(SPrintF1 "Got tx: %A" _txid)

                                    let closedChannelAfterMutualClosePerformed =
                                        { closedChannelAfterClosingSignedReceived with
                                              ActiveChannel =
                                                  { ConnectedChannel =
                                                        { connectedChannelAfterClosingSignedReceived with
                                                              PeerWrapper = peerWrapperAfterClosingSignedReceived
                                                              ChannelWrapper = channelWrapperAfterClosingSignedResponse } } }

                                    closedChannelAfterMutualClosePerformed.ActiveChannel.ConnectedChannel.SaveToWallet()

                                    return Ok closedChannelAfterMutualClosePerformed
                                | _ ->
                                    // This should never happen
                                    return Error <| UnexpectedApplyClosingSignedResult
                            | Error err -> return Error <| ApplyClosingSignedFailed err
                        | _ -> return Error <| ExpectedClosingSignedMsg channelMsg
                }

            let! result = exchange self

            return result
        }

    static member CheckClosingFinished(fundingTxId: TxId): Async<Result<bool, CloseChannelError>> =
        async {
            let fundingTxIdHash = fundingTxId.Value.ToString()

            let! fundingVerboseTransaction =
                QueryBTCFast(ElectrumClient.GetBlockchainTransactionVerbose fundingTxIdHash)

            let parsedTransaction =
                NBitcoin.Transaction.Parse(fundingVerboseTransaction.Hex, NBitcoin.Network.Main)

            let maybeOutput =
                try
                    // TODO: Find a better heuristic to check if this is the output we are looking for
                    Some(parsedTransaction.Outputs.Find(fun o -> o.ScriptPubKey.IsScriptType ScriptType.Witness))
                with :? System.ArgumentNullException -> None

            match maybeOutput with
            | None ->
                Infrastructure.LogDebug "Did not find an output with the correct type"
                return Ok false
            | Some output ->
                let sha =
                    NBitcoin.Crypto.Hashes.SHA256(output.ScriptPubKey.ToBytes())

                let reversedSha = sha.Reverse().ToArray()

                let scripthash =
                    NBitcoin.DataEncoders.Encoders.Hex.EncodeData reversedSha

                let! unspentOutputs = QueryBTCFast(ElectrumClient.GetUnspentTransactionOutputs scripthash)

                let outputIsUnspent =
                    Array.exists (fun o -> o.Value = output.Value.Satoshi) unspentOutputs

                return Ok(not outputIsUnspent)
        }
