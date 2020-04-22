namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.IO
open System.Net

open NBitcoin
open DotNetLightning.Channel
open DotNetLightning.Utils
open DotNetLightning.Chain
open DotNetLightning.Crypto
open DotNetLightning.Transactions
open DotNetLightning.Serialize

open GWallet.Backend
open GWallet.Backend.UtxoCoin.Lightning.Util

open Newtonsoft.Json

type LightningConfig = {
    LocalEndpoint: IPEndPoint
} with
    static member ConfigDir: DirectoryInfo =
        Config.GetConfigDir Currency.BTC AccountKind.Normal
    static member LightningDir: DirectoryInfo =
        Path.Combine (LightningConfig.ConfigDir.FullName, "LN") |> DirectoryInfo
    static member ConfigFileName = "config.json"
    static member ConfigFilePath =
        Path.Combine(LightningConfig.LightningDir.FullName, LightningConfig.ConfigFileName)

    static member Default(): LightningConfig = {
        LocalEndpoint =
            let ipAddress = IPAddress.Parse "0.0.0.0"
            let port = 9735
            IPEndPoint(ipAddress, port)
    }

    static member Load(): LightningConfig =
        if not (FileInfo LightningConfig.ConfigFilePath).Exists then
            let lightningConfig = LightningConfig.Default()
            lightningConfig.Save()
        let json = File.ReadAllText LightningConfig.ConfigFilePath
        Marshalling.DeserializeCustom<LightningConfig>(json, JsonMarshalling.SerializerSettings)

    member this.Save() =
        let json = Marshalling.SerializeCustom(this, JsonMarshalling.SerializerSettings)
        if not LightningConfig.LightningDir.Exists then
            LightningConfig.LightningDir.Create()
        File.WriteAllText(LightningConfig.ConfigFilePath, json)

