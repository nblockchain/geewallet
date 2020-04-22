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
        | WaitingForConfirmations of BlockHeightOffset32
        | FundingConfirmed
        | InvalidChannelState

    let GetSerializedChannelStatus (serializedChannel: SerializedChannel)
                                       : Async<ChannelStatus> = async {
        match serializedChannel.ChanState with
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

    let ListAvailableChannelIds(isFunder: bool): seq<ChannelId> = seq {
        for channelId in SerializedChannel.ListSavedChannels() do
            let serializedChannel = SerializedChannel.LoadFromWallet channelId
            if serializedChannel.IsFunder = isFunder then
                let channelStatus =
                    GetSerializedChannelStatus serializedChannel
                    |> Async.RunSynchronously
                match channelStatus with
                | ChannelStatus.Active -> yield channelId
                | _ -> ()
    }

