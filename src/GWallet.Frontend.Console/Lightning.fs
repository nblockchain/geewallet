namespace GWallet.Frontend.Console

open System
open System.IO

open DotNetLightning.Utils
open DotNetLightning.Channel
open GWallet.Backend
open GWallet.Backend.UtxoCoin.Lightning

module Lightning =
    let GetLightningChannelId(): Option<ChannelId> =
        SerializedChannel.ListSavedChannels() |> Seq.tryHead

    let GetLightningChannelAccount(channelId: ChannelId): UtxoCoin.NormalUtxoAccount =
        let serializedChannel = SerializedChannel.LoadFromWallet channelId
        serializedChannel.Account()

    let GetLightningNodeSecret (account: UtxoCoin.NormalUtxoAccount)
                               (password: string)
                                   : NBitcoin.ExtKey =
        let privateKeyByteLength = 32
        let privateKey = UtxoCoin.Account.GetPrivateKey account password
        let bytes: array<byte> = Array.zeroCreate privateKeyByteLength
        use bytesStream = new MemoryStream(bytes)
        let stream = NBitcoin.BitcoinStream(bytesStream, true)
        privateKey.ReadWrite stream
        NBitcoin.ExtKey bytes

    let StopLightning(transportListener: TransportListener): unit =
        (transportListener :> IDisposable).Dispose()

    type ChannelStatus =
        | Active
        | Closing
        | Closed
        | WaitingForConfirmations of BlockHeightOffset32
        | FundingConfirmed
        | InvalidChannelState

    let GetSerializedChannelStatus (serializedChannel: SerializedChannel)
                                       : Async<ChannelStatus> = async {
        match serializedChannel.ChanState with
        | ChannelState.Negotiating _
        | ChannelState.Closing _ ->
            return Closing
        | ChannelState.Closed _ ->
            return Closed
        | ChannelState.Normal _ -> return ChannelStatus.Active
        | ChannelState.WaitForFundingConfirmed waitForFundingConfirmedData ->
            let! confirmationCount =
                let txId = waitForFundingConfirmedData.Commitments.FundingScriptCoin.Outpoint.Hash.ToString()
                UtxoCoin.Server.Query
                    Currency.BTC
                    (UtxoCoin.QuerySettings.Default ServerSelectionMode.Fast)
                    (UtxoCoin.ElectrumClient.GetConfirmations txId)
                    None
            let minConfirmations = serializedChannel.MinSafeDepth.Value
            if confirmationCount < minConfirmations then
                let remainingConfirmations =
                    BlockHeightOffset32 <| minConfirmations - confirmationCount
                return ChannelStatus.WaitingForConfirmations remainingConfirmations
            else
                return ChannelStatus.FundingConfirmed
        | _ -> return ChannelStatus.InvalidChannelState
    }

    let ListAvailableChannelIds(isFunderOpt: Option<bool>): seq<ChannelId> = seq {
        for channelId in SerializedChannel.ListSavedChannels() do
            let serializedChannel = SerializedChannel.LoadFromWallet channelId
            match serializedChannel.ChanState with
            | ChannelState.Closed _ | ChannelState.Closing _ | ChannelState.Negotiating _ -> ()
            | _ ->
                match isFunderOpt with
                // This check below is required because of a bug where
                // None passed to this function is null
                | _ when Object.ReferenceEquals(isFunderOpt, null) ->
                    let channelStatus =
                        GetSerializedChannelStatus serializedChannel
                        |> Async.RunSynchronously
                    match channelStatus with
                    | ChannelStatus.Active -> yield channelId
                    | _ -> ()
                | None
                | Some _ when isFunderOpt.Value = serializedChannel.IsFunder  ->
                    let channelStatus =
                        GetSerializedChannelStatus serializedChannel
                        |> Async.RunSynchronously
                    match channelStatus with
                    | ChannelStatus.Active -> yield channelId
                    | _ -> ()
                | _ -> ()
    }
