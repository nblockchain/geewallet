namespace GWallet.Backend

open System
open System.IO
open System.Reflection

module internal Config =

    // we might want to test with TestNet at some point, so this below is the key:
    // (but we would need to get a seed list of testnet electrum servers, and testnet(/ropsten/rinkeby?), first...)
    let BitcoinNet = NBitcoin.Network.Main
    let LitecoinNet = NBitcoin.Altcoins.Litecoin.Instance.Mainnet
    let EtcNet = Nethereum.Signer.Chain.ClassicMainNet
    let EthNet = Nethereum.Signer.Chain.MainNet

    let internal DebugLog =
#if DEBUG
        true
#else
        false
#endif

    let GetMonoVersion(): Option<Version> =
        let maybeMonoRuntime = Type.GetType "Mono.Runtime" |> Option.ofObj
        match maybeMonoRuntime with

        // this would happen in MS.NET (e.g. UWP/WPF)
        | None -> None

        | Some monoRuntime ->
            let maybeDisplayName =
                monoRuntime.GetMethod("GetDisplayName", BindingFlags.NonPublic ||| BindingFlags.Static) |> Option.ofObj

            match maybeDisplayName with
            // this would happen in Mono Android/iOS/macOS
            | None -> None

            | Some displayName ->
                // example: 5.12.0.309 (2018-02/39d89a335c8 Thu Sep 27 06:54:53 EDT 2018)
                let fullVersion = displayName.Invoke(null, null) :?> string
                let simpleVersion = fullVersion.Substring(0, fullVersion.IndexOf(' ')) |> Version
                simpleVersion |> Some

    // TODO: move to FaultTolerantParallelClient
    let internal DEFAULT_NETWORK_TIMEOUT = TimeSpan.FromSeconds 60.0

    let internal NUMBER_OF_RETRIES_TO_SAME_SERVERS = uint16 1

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

    let private GetConfigDirForNormalAccountsOfThisCurrency(currency: Currency) =
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
        let configDirForNormalAccounts = GetConfigDirForNormalAccountsOfThisCurrency(currency)

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

    let private GetFile (account: IAccount): FileInfo =
        let configDir, fileName =
            match account with
            | :? NormalAccount as normalAccount ->
                let configDir = GetConfigDirForNormalAccountsOfThisCurrency account.Currency
                configDir, normalAccount.AccountFile.Name
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
        Path.Combine(configDir.FullName, fileName) |> FileInfo

    let AddNormalAccount conceptAccount: FileInfo =
        let configDir = GetConfigDirForNormalAccountsOfThisCurrency conceptAccount.Currency
        let fileName,jsonStoreContent = conceptAccount.FileNameAndContent
        let newAccountFile = Path.Combine(configDir.FullName, fileName)
        File.WriteAllText(newAccountFile, jsonStoreContent)
        FileInfo(newAccountFile)

    let RemoveNormal (account: NormalAccount): unit =
        let configFile = GetFile account
        if not configFile.Exists then
            failwithf "File %s doesn't exist. Please report this issue." configFile.FullName
        else
            configFile.Delete()

    let AddReadonly (account: ReadOnlyAccount): FileInfo =
        let configFile = GetFile account
        if configFile.Exists then
            raise AccountAlreadyAdded
        File.WriteAllText(configFile.FullName, String.Empty)
        configFile

    let RemoveReadonly (account: ReadOnlyAccount): unit =
        let configFile = GetFile account
        if not configFile.Exists then
            failwithf "File %s doesn't exist. Please report this issue." configFile.FullName
        else
            configFile.Delete()

    let AddArchivedAccount currency fileName unencryptedPrivateKey: FileInfo =
        let configDir = GetConfigDirForArchivedAccountsOfThisCurrency currency
        let newAccountFile = Path.Combine(configDir.FullName, fileName)

        // there's no ETH unencrypted standard: https://github.com/ethereum/wiki/wiki/Web3-Secret-Storage-Definition
        // ... so we simply write the private key in string format
        File.WriteAllText(newAccountFile, unencryptedPrivateKey)

        FileInfo(newAccountFile)
