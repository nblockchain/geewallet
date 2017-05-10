namespace GWallet.Backend

open System
open System.IO

type Currency = string
type PrivateKey = string
type CurrencyAccount = Currency * PrivateKey

module Config =

    let GetConfigPathForThisProgram() =
        let configPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        let configDir = DirectoryInfo(Path.Combine(configPath, "gwallet"))
        if not configDir.Exists then
            configDir.Create()
        configDir

    let Add(currencyAccount: CurrencyAccount) =
        let configFile = Path.Combine(GetConfigPathForThisProgram().FullName, fst currencyAccount)
        File.WriteAllText(configFile, snd currencyAccount)

