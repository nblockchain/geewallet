namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.IO

open NBitcoin
open DotNetLightning.Utils
open DotNetLightning.Channel
open DotNetLightning.Channel.ClosingHelpers
open Newtonsoft.Json

open GWallet.Backend
open GWallet.Backend.UtxoCoin
open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks

type HtlcSpendingTx =
    {
        ClosingTx: Transaction
        DelayedTxId: TransactionIdentifier
    }

type ChannelHtlcsData =
    {
        ChannelId: ChannelIdentifier
        ChannelHtlcsData: List<HtlcTransaction>
        ClosingTxOpt: Option<string>
    }

type HtlcsDataStore(account: NormalUtxoAccount) =
    static member HtlcDataFilePrefix = "htlcs-"
    static member HtlcDataFileEnding = ".json"

    member val Account = account
    member val Currency = (account :> IAccount).Currency

    member private self.AccountDir: DirectoryInfo =
        Config.GetConfigDir self.Currency AccountKind.Normal

    member private self.ChannelDir: DirectoryInfo =
        Path.Combine (self.AccountDir.FullName, Settings.ConfigDirName)
        |> DirectoryInfo

    member self.HtlcsDataFileName (channelId: ChannelIdentifier): string =
        Path.Combine(
            self.ChannelDir.FullName,
            SPrintF3
                "%s%s%s"
                HtlcsDataStore.HtlcDataFilePrefix
                (channelId.ToString())
                HtlcsDataStore.HtlcDataFileEnding
        )

    member internal self.LoadHtlcsData(channelId: ChannelIdentifier): ChannelHtlcsData =
        try
            let fileName = self.HtlcsDataFileName channelId
            let json = File.ReadAllText fileName
            Marshalling.DeserializeCustom<ChannelHtlcsData> (
                json,
                JsonMarshalling.SerializerSettings
            )
        with
        | :? FileNotFoundException | :? DirectoryNotFoundException ->
            {
                ChannelHtlcsData.ChannelId = channelId
                ClosingTxOpt = None
                ChannelHtlcsData = []
            }

    // For now all lightning incoming messages are handled within a single thread, we don't need a lock here.
    member internal self.SaveHtlcsData (serializedHtlcsData: ChannelHtlcsData) =
        let fileName = self.HtlcsDataFileName serializedHtlcsData.ChannelId
        let json =
            Marshalling.SerializeCustom(
                serializedHtlcsData,
                JsonMarshalling.SerializerSettings,
                Marshalling.DefaultFormatting
            )
        if not self.ChannelDir.Exists then
            self.ChannelDir.Create()
        File.WriteAllText(fileName, json)

