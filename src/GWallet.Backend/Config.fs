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

    let private GetConfigDirForThisCurrency(currency: Currency) =
        let configDir = DirectoryInfo(Path.Combine(GetConfigDirForWallets().FullName, currency.ToString()))
        if not configDir.Exists then
            configDir.Create()
        configDir

    let GetAllAccounts(currency: Currency): seq<Account> =
        let configDir = GetConfigDirForThisCurrency(currency)
        seq {
            for file in Directory.GetFiles(configDir.FullName) do
                let privKey = File.ReadAllText(file)
                let guid = Guid.Parse(Path.GetFileName(file))
                yield ({ Id = guid; HexPrivateKey = privKey; Currency = currency })
        }

    let Add(account: Account) =
        let configDir = GetConfigDirForThisCurrency(account.Currency)
        let configFile = Path.Combine(configDir.FullName, account.Id.ToString())
        File.WriteAllText(configFile, account.HexPrivateKey)

