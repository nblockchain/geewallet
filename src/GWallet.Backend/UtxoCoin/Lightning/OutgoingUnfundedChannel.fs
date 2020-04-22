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

type OutgoingUnfundedChannel = {
    ConnectedChannel: ConnectedChannel
    AcceptChannel: AcceptChannel
} with
    static member OpenChannel (peerWrapper: PeerWrapper)
                              (account: NormalUtxoAccount)
                              (channelCapacity: TransferAmount)
                              (metadata: TransactionMetadata)
                              (password: string)
                                  : Async<Result<OutgoingUnfundedChannel, PeerWrapper * PeerErrorMessage>> = async {
        let hex = DataEncoders.HexEncoder()
        let negotiatedFeeRatePerKw = FeeRatePerKw 10000u

        let nodeId = peerWrapper.MsgStream.TransportStream.Peer.TheirNodeId.Value
        let nodeSecret = peerWrapper.NodeSecret
        let channelIndex =
            let random = Org.BouncyCastle.Security.SecureRandom() :> Random
            random.Next(1, Int32.MaxValue / 2)
        let channelWrapper =
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
                    let indexes = List.ofSeq <| seq {
                        for indexedOutput in indexedOutputs do
                            if indexedOutput.TxOut.IsTo(dest) then
                                yield indexedOutput.N
                    }
                    if indexes.Length <> 1 then
                        failwith <| SPrintF1
                            "error determining funding output index.\
                            %i outputs were sent to the funding destination"
                            indexes.Length
                    TxOutIndex (uint16 indexes.[0])

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
        let openChannelMsgRes, channelWrapper =
            let channelCommand =
                let inputInitFunder = {
                    InputInitFunder.PushMSat = LNMoney.MilliSatoshis 0L
                    TemporaryChannelId = temporaryChannelId
                    FundingSatoshis = Money (channelCapacity.ValueToSend, MoneyUnit.BTC)
                    InitFeeRatePerKw = negotiatedFeeRatePerKw
                    FundingTxFeeRatePerKw = negotiatedFeeRatePerKw
                    LocalParams = localParams
                    RemoteInit = peerWrapper.Init
                    ChannelFlags = 0uy
                    ChannelKeys = channelWrapper.ChannelKeys
                }
                ChannelCommand.CreateOutbound inputInitFunder
            channelWrapper.ExecuteCommand channelCommand <| function
                | (NewOutboundChannelStarted(openChannelMsg, _)::[]) -> Some openChannelMsg
                | _ -> None
        let openChannelMsg = Unwrap openChannelMsgRes "error executing open channel command"

        let! peerWrapper = peerWrapper.SendMsg openChannelMsg

        // receive accept_channel
        DebugLogger "Receiving accept_channel..."
        let! peerWrapper, channelMsgRes = peerWrapper.RecvChannelMsg()
        match channelMsgRes with
        | Error errorMessage ->
            return Error (peerWrapper, errorMessage)
        | Ok channelMsg ->
            let acceptChannelMsg =
                match channelMsg with
                | :? AcceptChannel as acceptChannelMsg -> acceptChannelMsg
                | msg -> raise <| UnexpectedMsg(["AcceptChannel"], msg)

            let minimumDepth = acceptChannelMsg.MinimumDepth
            let connectedChannel = {
                PeerWrapper = peerWrapper
                ChannelWrapper = channelWrapper
                Account = account
                MinimumDepth = minimumDepth
                ChannelIndex = channelIndex
            }
            let outgoingUnfundedChannel = {
                ConnectedChannel = connectedChannel
                AcceptChannel = acceptChannelMsg
            }
            return Ok outgoingUnfundedChannel
    }

    member this.MinimumDepth
        with get(): BlockHeightOffset32 = this.ConnectedChannel.MinimumDepth

    member this.ChannelId
        with get(): ChannelId = this.ConnectedChannel.ChannelId

