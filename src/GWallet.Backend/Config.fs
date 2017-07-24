namespace GWallet.Backend

open System
open System.IO

open Nethereum.KeyStore
open Nethereum.Signer

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

    let private GetConfigDirForArchivedAccountsOfThisCurrency(currency: Currency) =
        let configDir = DirectoryInfo(Path.Combine(GetConfigDirForAccounts().FullName,
                                                   "archived", currency.ToString()))
        if not configDir.Exists then
            configDir.Create()
        configDir

    let GetAllNormalAccounts(currency: Currency): seq<FileInfo> =
        let configDirForNormalAccounts = GetConfigDirForAccountsOfThisCurrency(currency)

        seq {
            for filePath in Directory.GetFiles(configDirForNormalAccounts.FullName) do
                yield FileInfo(filePath)
        }

    let GetAllReadOnlyAccounts(currency: Currency): seq<FileInfo> =
        let configDirForReadonlyAccounts = GetConfigDirForReadonlyAccountsOfThisCurrency(currency)

        seq {
            for filePath in Directory.GetFiles(configDirForReadonlyAccounts.FullName) do
                yield FileInfo(filePath)
        }

    let GetAllArchivedAccounts(currency: Currency): seq<FileInfo> =
        let configDirForArchivedAccounts = GetConfigDirForArchivedAccountsOfThisCurrency(currency)

        seq {
            for filePath in Directory.GetFiles(configDirForArchivedAccounts.FullName) do
                yield FileInfo(filePath)
        }

    let private GetFile (account: IAccount) =
        let configDir, fileName =
            match account with
            | :? NormalAccount as normalAccount ->
                normalAccount.AccountFile.Directory, normalAccount.AccountFile.Name
            | :? ReadOnlyAccount as readOnlyAccount ->
                let configDir = GetConfigDirForReadonlyAccountsOfThisCurrency(account.Currency)
                let fileName = account.PublicAddress
                configDir, fileName
            | :? ArchivedAccount as archivedAccount ->
                let configDir = GetConfigDirForArchivedAccountsOfThisCurrency(account.Currency)
                let fileName = account.PublicAddress
                configDir, fileName
            | _ -> failwith (sprintf "Account type not valid for archiving: %s. Please report this issue."
                       (account.GetType().FullName))
        Path.Combine(configDir.FullName, fileName)

    let AddNormalAccount currency fileName jsonStoreContent =
        let configDir = GetConfigDirForAccountsOfThisCurrency(currency)
        let newAccountFile = Path.Combine(configDir.FullName, fileName)
        File.WriteAllText(newAccountFile, jsonStoreContent)
        FileInfo(newAccountFile)

    let RemoveNormal (account: NormalAccount) =
        let configFile = GetFile account
        if not (File.Exists configFile) then
            failwith (sprintf "File %s doesn't exist. Please report this issue." configFile)
        else
            File.Delete(configFile)

    let AddReadonly (account: ReadOnlyAccount) =
        let configFile = GetFile account
        File.WriteAllText(configFile, String.Empty)

    let RemoveReadonly (account: ReadOnlyAccount) =
        let configFile = GetFile account
        if not (File.Exists configFile) then
            failwith (sprintf "File %s doesn't exist. Please report this issue." configFile)
        else
            File.Delete(configFile)

    let AddArchived (account: ArchivedAccount) =
        let configFile = GetFile account

        // there's no unencrypted standard: https://github.com/ethereum/wiki/wiki/Web3-Secret-Storage-Definition
        // ... so we simply write the private key in string format
        File.WriteAllText(configFile, account.PrivateKey.GetPrivateKey())
