namespace GWallet.Backend

open System
open System.IO
open System.Linq
open System.Reflection
open System.Runtime.InteropServices

open Fsdk

open GWallet.Backend.FSharpUtil.UwpHacks

// TODO: make internal when tests don't depend on this anymore
module Config =

    [<Literal>]
    let AppName = "geewallet"

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

    // NOTE: enabling this might look confusing because it only works for non-cache
    //       balances, so you might find discrepancies (e.g. the donut-chart-view)
    let internal NoNetworkBalanceForDebuggingPurposes = false

    let internal UseNativeSegwit =
        true

    let IsWindowsPlatform() =
        RuntimeInformation.IsOSPlatform OSPlatform.Windows

    let IsMacPlatform() =
        RuntimeInformation.IsOSPlatform OSPlatform.OSX

    let GetMonoVersion(): Option<Version> =
        FSharpUtil.option {
            // this gives None on MS.NET (e.g. UWP/WPF)
            let! monoRuntime = Type.GetType "Mono.Runtime" |> Option.ofObj
            // this gives None on Mono Android/iOS/macOS
            let! displayName =
                monoRuntime.GetMethod("GetDisplayName", BindingFlags.NonPublic ||| BindingFlags.Static) |> Option.ofObj
                // example: 5.12.0.309 (2018-02/39d89a335c8 Thu Sep 27 06:54:53 EDT 2018)
            let fullVersion = displayName.Invoke(null, null) :?> string
            let simpleVersion = fullVersion.Substring(0, fullVersion.IndexOf(' ')) |> Version
            return simpleVersion
        }

    // FIXME: make FaultTolerantParallelClient accept funcs that receive this as an arg, maybe 2x-ing it when a full
    //        round of failures has happened, as in, all servers failed
    let internal DEFAULT_NETWORK_TIMEOUT = TimeSpan.FromSeconds 30.0
    let internal DEFAULT_NETWORK_CONNECT_TIMEOUT = TimeSpan.FromSeconds 5.0

    let internal NUMBER_OF_RETRIES_TO_SAME_SERVERS = 3u

    let private isWindows =
        Path.DirectorySeparatorChar = '\\'

    let internal GetConfigDirForThisProgram() =
        let configPath =
(* NOTE: we used to support UWP via the Xamarin.Essentials code below, but MAUI is a higher priority than resurrecting UWP now:
            if (not isWindows) || Xamarin.Essentials.DeviceInfo.Platform <> Xamarin.Essentials.DevicePlatform.UWP then
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            else //UWP
                Xamarin.Essentials.FileSystem.AppDataDirectory
*)
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)

        // TODO: rename to "geewallet", following a similar approach as DAI->SAI rename
        let configDir = DirectoryInfo(Path.Combine(configPath, "gwallet"))
        if not configDir.Exists then
            configDir.Create()
        configDir

    let internal GetCacheDir() =
        let configPath = GetConfigDirForThisProgram().FullName
        let configDir = DirectoryInfo(Path.Combine(configPath, "cache"))
        if not configDir.Exists then
            configDir.Create()
        configDir

    let private GetConfigDirForAccounts() =
        let configPath = GetConfigDirForThisProgram().FullName
        let configDir = DirectoryInfo(Path.Combine(configPath, "accounts"))
        if not configDir.Exists then
            configDir.Create()
        configDir

    let private GetConfigDirInternal (currency: Currency) (accountKind: AccountKind) (createIfNotAlreadyExisting: bool): Option<DirectoryInfo> =
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
            if createIfNotAlreadyExisting then
                configDir.Create()
                Some configDir
            else
                None
        else
            Some configDir

    let private GetConfigDir (currency: Currency) (accountKind: AccountKind) =
        match GetConfigDirInternal currency accountKind true with
        | Some dir -> dir
        | None -> failwith "Unreachable, after invoking with createIfNotAlreadyExisting=true, it should return Some"

    let GetAccountFilesWithCurrency (currency: Currency) (accountKind: AccountKind): seq<FileRepresentation> =
        seq {
            for filePath in Directory.GetFiles (GetConfigDir currency accountKind).FullName do
                yield FileRepresentation.FromFile (FileInfo filePath)
        }

    let GetAccountFiles (currencies: seq<Currency>) (accountKind: AccountKind): seq<FileRepresentation> =
        seq {
            for currency in currencies do
                for accountFile in GetAccountFilesWithCurrency currency accountKind do
                    yield accountFile
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

    let public Wipe (): unit =
        let configDirForAccounts = GetConfigDirForAccounts()
        Directory.Delete(configDirForAccounts.FullName, true)

    // we don't expose this as public because we don't want to allow removing archived accounts
    let private RemoveAccount (account: BaseAccount): unit =
        let configFile = GetFile (account:>IAccount).Currency account
        if not configFile.Exists then
            failwith <| SPrintF1 "File %s doesn't exist. Please report this issue." configFile.FullName
        else
            configFile.Delete()

    let RemoveNormalAccount (account: NormalAccount): unit =
        RemoveAccount account

    let RemoveReadOnlyAccount (account: ReadOnlyAccount): unit =
        RemoveAccount account

    /// NOTE: the real initialization happens in Account.fs , see isInitialized
    let internal Init() =
        // In case a new token was added it will not have a config for an existing user
        // we copy the eth configs to the new tokens config directory
        let propagateEthAccountInfoToMissingTokensAccounts() =
            for accountKind in (AccountKind.All()) do
                let ethConfigDir = GetConfigDir Currency.ETH accountKind
                for token in Currency.GetAll() do
                    if token.IsEthToken() then
                        let maybeTokenConfigDir = GetConfigDirInternal token accountKind false
                        match maybeTokenConfigDir with
                        | Some _ ->
                            // already removed token account before
                            ()
                        | None ->
                            // now create it if it wasn't there before
                            let tokenConfigDir = GetConfigDir token accountKind
                            for ethAccountFilePath in Directory.GetFiles ethConfigDir.FullName do
                                let newPath = ethAccountFilePath.Replace(ethConfigDir.FullName, tokenConfigDir.FullName)
                                if not (File.Exists newPath) then
                                    File.Copy(ethAccountFilePath, newPath)

        propagateEthAccountInfoToMissingTokensAccounts()
