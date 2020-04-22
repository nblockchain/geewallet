namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Diagnostics

open NBitcoin

open DotNetLightning.Utils
open DotNetLightning.Serialize.Msgs
open DotNetLightning.Chain
open DotNetLightning.Channel
open DotNetLightning.Transactions

open GWallet.Backend
open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks
open GWallet.Backend.UtxoCoin
open GWallet.Backend.UtxoCoin.Lightning.Util

open FSharp.Core

type OpenChannelError =
    | InvalidChannelParameters of PeerWrapper * ChannelError
    | RecvAcceptChannel of RecvMsgError
    | OpenChannelPeerErrorResponse of PeerWrapper * PeerErrorMessage
    | ExpectedAcceptChannel of ILightningMsg
    with
    member this.Message =
        match this with
        | InvalidChannelParameters (_, err) ->
            SPrintF1 "Invalid channel parameters: %s" err.Message
        | RecvAcceptChannel err ->
            SPrintF1 "Error receiving accept_channel: %s" err.Message
        | OpenChannelPeerErrorResponse (_, err) ->
            SPrintF1 "Peer responded to our open_channel with an error message: %s" err.Message
        | ExpectedAcceptChannel msg ->
            SPrintF1 "Expected accept_channel, got %A" (msg.GetType())
    member this.PossibleBug =
        match this with
        | RecvAcceptChannel err -> err.PossibleBug
        | InvalidChannelParameters _
        | OpenChannelPeerErrorResponse _
        | ExpectedAcceptChannel _ -> false

type OutgoingUnfundedChannel = {
    ConnectedChannel: ConnectedChannel
    AcceptChannelMsg: AcceptChannelMsg
} with
    static member OpenChannel (peerWrapper: PeerWrapper)
                              (account: NormalUtxoAccount)
                              (channelCapacity: TransferAmount)
                              (metadata: TransactionMetadata)
                              (password: string)
                                  : Async<Result<OutgoingUnfundedChannel, OpenChannelError>> = async {
        let hex = DataEncoders.HexEncoder()

        let nodeId = peerWrapper.RemoteNodeId
        let nodeSecret = peerWrapper.NodeSecret
        let channelIndex =
            let random = Org.BouncyCastle.Security.SecureRandom() :> Random
            random.Next(1, Int32.MaxValue / 2)
        let! channelWrapper =
            let fundingTxProvider (dest: IDestination, amount: Money, _feeRate: FeeRatePerKw) =
                Debug.Assert(amount.ToDecimal MoneyUnit.BTC = channelCapacity.ValueToSend)
                let transactionHex =
                    UtxoCoin.Account.SignTransactionForDestination
                        account
                        metadata
                        dest
                        channelCapacity
                        password
                let fundingTransaction = Transaction.Load (hex.DecodeData transactionHex, Config.BitcoinNet)
                let fundingOutputIndex =
                    let indexedOutputs = fundingTransaction.Outputs.AsIndexedOutputs()
                    let hasRightDestination (indexedOutput: IndexedTxOut): bool =
                        indexedOutput.TxOut.IsTo dest
                    let matchingOutput: IndexedTxOut =
                        Seq.find hasRightDestination indexedOutputs
                    TxOutIndex <| uint16 matchingOutput.N
                let fundingTransaction =
                    Transaction.Load (hex.DecodeData transactionHex, Config.BitcoinNet)
                    |> FinalizedTx
                Ok (fundingTransaction, fundingOutputIndex)
            ChannelWrapper.Create
                nodeId
                (Account.CreatePayoutScript account)
                nodeSecret
                channelIndex
                fundingTxProvider
                WaitForInitInternal
        let localParams =
            let funding = Money(channelCapacity.ValueToSend, MoneyUnit.BTC)
            let defaultFinalScriptPubKey = Account.CreatePayoutScript account
            channelWrapper.LocalParams funding defaultFinalScriptPubKey true
        let temporaryChannelId =
            let random = Org.BouncyCastle.Security.SecureRandom() :> Random
            let temporaryChannelIdBytes: array<byte> = Array.zeroCreate 32
            random.NextBytes temporaryChannelIdBytes
            temporaryChannelIdBytes |> uint256 |> ChannelId
        let feeRate =
            channelWrapper.Channel.FeeEstimator.GetEstSatPer1000Weight ConfirmationTarget.Normal
        let openChannelMsgRes, channelWrapperAfterOpenChannel =
            let channelCommand =
                let inputInitFunder = {
                    InputInitFunder.PushMSat = LNMoney.MilliSatoshis 0L
                    TemporaryChannelId = temporaryChannelId
                    FundingSatoshis = Money (channelCapacity.ValueToSend, MoneyUnit.BTC)
                    InitFeeRatePerKw = feeRate
                    FundingTxFeeRatePerKw = feeRate
                    LocalParams = localParams
                    RemoteInit = peerWrapper.InitMsg
                    ChannelFlags = 0uy
                    ChannelKeys = channelWrapper.ChannelKeys
                }
                ChannelCommand.CreateOutbound inputInitFunder
            channelWrapper.ExecuteCommand channelCommand <| function
                | (NewOutboundChannelStarted(openChannelMsg, _)::[]) -> Some openChannelMsg
                | _ -> None
        match openChannelMsgRes with
        | Error channelError ->
            return Error <| InvalidChannelParameters (peerWrapper, channelError)
        | Ok openChannelMsg ->
            let! peerWrapperAfterOpenChannel = peerWrapper.SendMsg openChannelMsg

            Infrastructure.LogDebug "Receiving accept_channel..."
            let! recvChannelMsgRes = peerWrapperAfterOpenChannel.RecvChannelMsg()
            match recvChannelMsgRes with
            | Error (RecvMsg recvMsgError) -> return Error <| RecvAcceptChannel recvMsgError
            | Error (ReceivedPeerErrorMessage (peerWrapperAfterAcceptChannel, errorMessage)) ->
                (peerWrapperAfterAcceptChannel :> IDisposable).Dispose()
                return Error <| OpenChannelPeerErrorResponse
                    (peerWrapperAfterAcceptChannel, errorMessage)
            | Ok (peerWrapperAfterAcceptChannel, channelMsg) ->
                match channelMsg with
                | :? AcceptChannelMsg as acceptChannelMsg ->
                    let minimumDepth = acceptChannelMsg.MinimumDepth
                    let connectedChannel = {
                        PeerWrapper = peerWrapperAfterAcceptChannel
                        ChannelWrapper = channelWrapperAfterOpenChannel
                        Account = account
                        MinimumDepth = minimumDepth
                        ChannelIndex = channelIndex
                    }
                    let outgoingUnfundedChannel = {
                        ConnectedChannel = connectedChannel
                        AcceptChannelMsg = acceptChannelMsg
                    }
                    return Ok outgoingUnfundedChannel
                | _ -> return Error <| ExpectedAcceptChannel channelMsg
    }

    member this.MinimumDepth
        with get(): BlockHeightOffset32 = this.ConnectedChannel.MinimumDepth

    member this.ChannelId
        with get(): ChannelId = this.ConnectedChannel.ChannelId

