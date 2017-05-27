namespace GWallet.Backend

open System
open System.IO

open Nethereum.KeyStore

module internal Config =

    let internal GetConfigDirForThisProgram() =
        let configPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        let configDir = DirectoryInfo(Path.Combine(configPath, "gwallet"))
        if not configDir.Exists then
            configDir.Create()
        configDir

    let private GetConfigDirForAccounts() =
        let configPath = GetConfigDirForThisProgram().FullName
        let configDir = DirectoryInfo(Path.Combine(configPath, "accounts"))
        if not configDir.Exists then
            configDir.Create()
        configDir

    let private GetConfigDirForAccountsOfThisCurrency(currency: Currency) =
        let configDir = DirectoryInfo(Path.Combine(GetConfigDirForAccounts().FullName, currency.ToString()))
        if not configDir.Exists then
            configDir.Create()
        configDir

    let private GetConfigDirForReadonlyAccountsOfThisCurrency(currency: Currency) =
        let configDir = DirectoryInfo(Path.Combine(GetConfigDirForAccounts().FullName,
                                                   "readonly", currency.ToString()))
        if not configDir.Exists then
            configDir.Create()
        configDir

    let GetAllAccounts(currency: Currency): seq<IAccount> =
        let configDirForNormalAccounts = GetConfigDirForAccountsOfThisCurrency(currency)
        let configDirForReadonlyAccounts = GetConfigDirForReadonlyAccountsOfThisCurrency(currency)
        seq {
            for filePath in Directory.GetFiles(configDirForNormalAccounts.FullName) do
                let json = File.ReadAllText(filePath)
                yield NormalAccount(currency, json) :> IAccount

            for filePath in Directory.GetFiles(configDirForReadonlyAccounts.FullName) do
                let fileName = Path.GetFileName(filePath)
                yield ReadOnlyAccount(currency, fileName) :> IAccount
        }

    let Add (account: NormalAccount) =
        let configDir = GetConfigDirForAccountsOfThisCurrency((account:>IAccount).Currency)
        let fileName = NormalAccount.KeyStoreService.GenerateUTCFileName((account:>IAccount).PublicAddress)
        let configFile = Path.Combine(configDir.FullName, fileName)
        File.WriteAllText(configFile, account.Json)

    let AddReadonly (account: ReadOnlyAccount) =
        let configDir = GetConfigDirForReadonlyAccountsOfThisCurrency((account:>IAccount).Currency)
        let fileName = (account:>IAccount).PublicAddress
        let configFile = Path.Combine(configDir.FullName, fileName)
        File.WriteAllText(configFile, String.Empty)

