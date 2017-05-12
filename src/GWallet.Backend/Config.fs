namespace GWallet.Backend

open System
open System.IO

module Config =

    let private GetConfigDirForThisProgram() =
        let configPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        let configDir = DirectoryInfo(Path.Combine(configPath, "gwallet"))
        if not configDir.Exists then
            configDir.Create()
        configDir

    let private GetConfigDirForWallets() =
        let configPath = GetConfigDirForThisProgram().FullName
        let configDir = DirectoryInfo(Path.Combine(configPath, "accounts"))
        if not configDir.Exists then
            configDir.Create()
        configDir

    let private GetConfigFileForThisCurrency(currency: Currency) =
        Path.Combine(GetConfigDirForWallets().FullName, currency.ToString())

    let GetMainAccount(currency: Currency): Option<Account> =
        let configFile = GetConfigFileForThisCurrency(currency)
        let maybePrivateKeyInHex: Option<string> =
            try
                Some(File.ReadAllText(configFile))
            with
            | :? FileNotFoundException -> None
        match maybePrivateKeyInHex with
        | Some(privKey) -> Some({ HexPrivateKey = privKey; Currency = currency })
        | None -> None

    let Add(account: Account) =
        File.WriteAllText(GetConfigFileForThisCurrency(account.Currency), account.HexPrivateKey)

