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
    | CloseCommandFailed of ChannelError
    | RecvFailed of RecvMsgError
    | RecvPeerError of BrokenChannel * PeerErrorMessage
    | ExpectedShutdownMsg of ILightningMsg
    | RemoteShutdownCommandFailed of BrokenChannel * ChannelError
    | ExpectedClosingSignedMsg of ILightningMsg
    | ApplyClosingSignedFailed of ChannelError
    member self.Message =
        match self with
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

type ClosedChannel()= 
    static member InitiateCloseChannel(connectedChannel: ConnectedChannel): Async<Result<ClosedChannel, CloseChannelError>> =
        async {
            let ourPayoutScript =
                connectedChannel.ChannelWrapper.Channel.Config.ChannelOptions.ShutdownScriptPubKey.Value

            let! shutdownSendResult = ClosedChannel.InitiateShutdown connectedChannel ourPayoutScript

            match shutdownSendResult with
            | Error e -> return Error <| e
            | Ok connectedChannelAfterShutdownSent ->
                let! closingSignedExchangeResult = ClosedChannel.RunClosingSignedExchange connectedChannelAfterShutdownSent ourPayoutScript

                match closingSignedExchangeResult with
                | Error e -> return Error <| e
                | Ok _ -> return Ok (ClosedChannel())
        }

    static member AwaitCloseChannel(connectedChannel: ConnectedChannel): Async<Result<ClosedChannel, CloseChannelError>> =
        async {
            let ourPayoutScript =
                connectedChannel.ChannelWrapper.Channel.Config.ChannelOptions.ShutdownScriptPubKey.Value

            let! shutdownReceiveResult = ClosedChannel.ReceiveShutdown connectedChannel

            match shutdownReceiveResult with
            | Error e -> return Error <| e
            | Ok connectedChannelAfterShutdownReceived ->
                let! closingSignedExchangeResult = ClosedChannel.RunClosingSignedExchange connectedChannelAfterShutdownReceived ourPayoutScript

                match closingSignedExchangeResult with
                | Error e -> return Error <| e
                | Ok _ -> return Ok (ClosedChannel())
        }

    static member private InitiateShutdown connectedChannel ourPayoutScript: Async<Result<ConnectedChannel, CloseChannelError>> =
        async {
            Infrastructure.LogDebug "Sending shutdown message"
            let channelWrapper = connectedChannel.ChannelWrapper
            let peerWrapper = connectedChannel.PeerWrapper
            match OperationClose.Create ourPayoutScript with
            | Error e -> return failwith (SPrintF1 "Failed to create OperationClose: %s" (e.ToString()))
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
                        let connectedChannelAfterShutdownChannelReceived =
                            { connectedChannel with
                                  ChannelWrapper = channelWrapperAfterCloseChannel
                                  PeerWrapper = peerWrapperAfterShutdownChannelReceived } 

                        match channelMsg with
                        | :? ShutdownMsg as shutdownMsg ->
                            return! (ClosedChannel.HandleRemoteShutdown connectedChannelAfterShutdownChannelReceived shutdownMsg true)
                        | _ -> return Error <| ExpectedShutdownMsg channelMsg
        }

    static member private ReceiveShutdown connectedChannel: Async<Result<ConnectedChannel, CloseChannelError>> =
        async {
            Infrastructure.LogDebug "Waiting for shutdown message"
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
                let connectedChannelAfterShutdownChannelReceived =
                    { connectedChannel with
                          PeerWrapper = peerWrapperAfterShutdownChannelReceived }

                match channelMsg with
                | :? ShutdownMsg as shutdownMsg ->
                    return! (ClosedChannel.HandleRemoteShutdown connectedChannelAfterShutdownChannelReceived shutdownMsg false)
                | _ -> return Error <| ExpectedShutdownMsg channelMsg
        }

    static member private HandleRemoteShutdown (connectedChannel: ConnectedChannel) (shutdownMsg: ShutdownMsg) (sentOurs: bool): Async<Result<ConnectedChannel, CloseChannelError>> =
        async {
            Infrastructure.LogDebug "Received remote shutdown message"

            let peerWrapperAfterShutdownChannelReceived = connectedChannel.PeerWrapper

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
                                   { connectedChannel with
                                         PeerWrapper = peerWrapperAfterClosingSigned
                                         ChannelWrapper = channelWrapperAfterShutdownResponse }
                    | None ->
                        Infrastructure.LogDebug "We are not funder, waiting for initial ClosingSigned from peer"

                        return Ok
                                   { connectedChannel with
                                         PeerWrapper = peerWrapperAfterShutdownResponse
                                         ChannelWrapper = channelWrapperAfterShutdownResponse }
        }

    static member private RunClosingSignedExchange connectedChannel ourPayoutScript: Async<Result<ConnectedChannel, CloseChannelError>> =
        async {
            Infrastructure.LogDebug "Starting closingSigned exchange loop"

            let rec exchange channel =
                async {
                    Infrastructure.LogDebug "Waiting for next closingSigned message from peer"
                    let recvChannelMsgRes = 
                        try
                            channel.PeerWrapper.RecvChannelMsg() |> Async.RunSynchronously
                        with
                        | :? System.IO.IOException
                        | :? System.Net.Sockets.SocketException
                        | :? System.AggregateException -> Error <| RecvMsg (RecvBytes (PeerDisconnected { Abruptly = false } ))

                    match recvChannelMsgRes with
                    | Error (RecvMsg _) ->
                        // We assume the peer closed the connection after broadcasting the tx
                        Infrastructure.LogDebug "Peer closed connection after successful closing negotiation"
                        channel.SaveToWallet()
                        return Ok channel
                    | Error (ReceivedPeerErrorMessage (peerWrapperAfterClosingSignedReceived, errorMessage)) ->
                        let connectedChannelAfterError =
                            { channel with
                                  PeerWrapper = peerWrapperAfterClosingSignedReceived }

                        let brokenChannel =
                            { BrokenChannel.ConnectedChannel = connectedChannelAfterError }

                        return Error
                               <| RecvPeerError(brokenChannel, errorMessage)
                    | Ok (peerWrapperAfterClosingSignedReceived, channelMsg) ->
                        let connectedChannelAfterClosingSignedReceived =
                            { channel with
                                  PeerWrapper = peerWrapperAfterClosingSignedReceived }

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

                                    let connectedChannelAfterClosingSignedResponse =
                                        { connectedChannelAfterClosingSignedReceived with
                                              PeerWrapper = peerWrapperAfterClosingSignedResponse
                                              ChannelWrapper = channelWrapperAfterClosingSignedResponse }

                                    let! result = exchange connectedChannelAfterClosingSignedResponse

                                    return result
                                | (None, Some finalizedTx) ->
                                    Infrastructure.LogDebug "Mutual close performed"
                                    Infrastructure.LogDebug(SPrintF1 "FinalizedTX: %A" finalizedTx)
                                    Infrastructure.LogDebug(SPrintF1 "ourPayoutScript: %A" ourPayoutScript)

                                    let! _txid =
                                        let signedTx: string = finalizedTx.Value.ToHex()
                                        Infrastructure.LogDebug(SPrintF1 "Broadcasting tx: %A" signedTx)
                                        Account.BroadcastRawTransaction Currency.BTC signedTx

                                    Infrastructure.LogDebug(SPrintF1 "Got tx: %A" _txid)

                                    let connectedChannelAfterMutualClosePerformed =
                                        { connectedChannelAfterClosingSignedReceived with
                                              PeerWrapper = peerWrapperAfterClosingSignedReceived
                                              ChannelWrapper = channelWrapperAfterClosingSignedResponse }

                                    connectedChannelAfterMutualClosePerformed.SaveToWallet()

                                    return Ok connectedChannelAfterMutualClosePerformed
                                | _ ->
                                    // This should never happen
                                    return failwith "Expected to receive either new closingSigned or mutualClosePerformed"
                            | Error err -> return Error <| ApplyClosingSignedFailed err
                        | _ -> return Error <| ExpectedClosingSignedMsg channelMsg
                }

            let! result = exchange connectedChannel

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
