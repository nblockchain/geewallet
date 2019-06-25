namespace GWallet.Backend

open System
open System.IO
open System.Linq
open System.Reflection

open Xamarin.Essentials

// TODO: make internal when tests don't depend on this anymore
module Config =

    // we might want to test with TestNet at some point, so this below is the key:
    // (but we would need to get a seed list of testnet electrum servers, and testnet(/ropsten/rinkeby?), first...)
    let BitcoinNet = NBitcoin.Network.Main
    let LitecoinNet = NBitcoin.Altcoins.Litecoin.Instance.Mainnet
    let EtcNet = Nethereum.Signer.Chain.ClassicMainNet
    let EthNet = Nethereum.Signer.Chain.MainNet

    // https://github.com/Nethereum/Nethereum/issues/509
    let EthTokenEstimationCouldBeBuggyAsInNotAccurate = true

    let internal DebugLog =
#if DEBUG
        true
#else
        false
#endif

    let internal NoNetworkBalanceForDebuggingPurposes = true

    let IsWindowsPlatform() =
        Path.DirectorySeparatorChar = '\\'

    let IsMacPlatform() =
        let macDirs = [ "/Applications"; "/System"; "/Users"; "/Volumes" ]
        match Environment.OSVersion.Platform with
        | PlatformID.MacOSX ->
            true
        | PlatformID.Unix ->
            if macDirs.All(fun dir -> Directory.Exists dir) then
                if not (DeviceInfo.Platform.Equals DevicePlatform.iOS) then
                    true
                else
                    false
            else
                false
        | _ ->
            false

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

    // TODO: make the tests instantiate Legacy or nonLegacyTcpClient themselves and test both from them
    let NewUtxoTcpClientDisabled =

        //in macOS, even if using Mono >5.18.0.240, we still get the same issue of receiving an empty string from the
        //"blockchain.scripthash.listunspent" stratum API, like: https://gitlab.com/knocte/geewallet/issues/54
        IsMacPlatform() ||

        //we need this check because older versions of Mono (such as 5.16, or Ubuntu 18.04 LTS's version: 4.6.2)
        //don't work with the new TCP client, only the legacy one works
        (Option.exists (fun monoVersion -> monoVersion < Version("5.18.0.240")) (GetMonoVersion()))

    // FIXME: make FaultTolerantParallelClient accept funcs that receive this as an arg, maybe 2x-ing it when a full
    //        round of failures has happened, as in, all servers failed
    let internal DEFAULT_NETWORK_TIMEOUT = TimeSpan.FromSeconds 30.0

    let internal NUMBER_OF_RETRIES_TO_SAME_SERVERS = 2u

    let private isWindows =
        Path.DirectorySeparatorChar = '\\'

    let internal GetConfigDirForThisProgram() =
        let configPath =
            if not isWindows then
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            else //UWP
                Xamarin.Essentials.FileSystem.AppDataDirectory

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

    let private GetConfigDir (currency: Currency) (accountKind: AccountKind) =
        let accountConfigDir = GetConfigDirForAccounts().FullName

        let baseConfigDir =
            match accountKind with
            | AccountKind.Normal ->
                accountConfigDir
            | AccountKind.ReadOnly ->
                Path.Combine(accountConfigDir, "readonly")
            | AccountKind.Archived ->
                Path.Combine(accountConfigDir, "archived")

        let configDir = Path.Combine(baseConfigDir, currency.ToString()) |> DirectoryInfo
        if not configDir.Exists then
            configDir.Create()
        configDir

    let GetAccountFiles (currencies: seq<Currency>) (accountKind: AccountKind): seq<FileRepresentation> =
        seq {
            for currency in currencies do
                for filePath in Directory.GetFiles (GetConfigDir currency accountKind).FullName do
                    yield {
                        Name = Path.GetFileName filePath
                        Content = (fun _ -> File.ReadAllText filePath)
                    }
        }

    let private GetFile (currency: Currency) (account: BaseAccount): FileInfo =
        let configDir, fileName = GetConfigDir currency account.Kind, account.AccountFile.Name
        Path.Combine(configDir.FullName, fileName) |> FileInfo

    let AddAccount (conceptAccount: ConceptAccount) (accountKind: AccountKind): FileRepresentation =
        let configDir = GetConfigDir conceptAccount.Currency accountKind
        let newAccountFile = Path.Combine(configDir.FullName, conceptAccount.FileRepresentation.Name) |> FileInfo
        if newAccountFile.Exists then
            raise AccountAlreadyAdded
        File.WriteAllText(newAccountFile.FullName, conceptAccount.FileRepresentation.Content())

        {
            Name = Path.GetFileName newAccountFile.FullName
            Content = fun _ -> File.ReadAllText newAccountFile.FullName
        }

    let internal Wipe(currency: Currency): unit =
        let configDirForAccounts = GetConfigDirForAccounts()
        Directory.Delete(configDirForAccounts.FullName, true) |> ignore

    // we don't expose this as public because we don't want to allow removing archived accounts
    let private RemoveAccount (account: BaseAccount): unit =
        let configFile = GetFile (account:>IAccount).Currency account
        if not configFile.Exists then
            failwithf "File %s doesn't exist. Please report this issue." configFile.FullName
        else
            configFile.Delete()

    let RemoveNormalAccount (account: NormalAccount): unit =
        RemoveAccount account

    let RemoveReadOnlyAccount (account: ReadOnlyAccount): unit =
        RemoveAccount account
