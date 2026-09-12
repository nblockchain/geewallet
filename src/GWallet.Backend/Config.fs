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

    let IsWindowsPlatform() =
        RuntimeInformation.IsOSPlatform OSPlatform.Windows

    let IsMacPlatform() =
        RuntimeInformation.IsOSPlatform OSPlatform.OSX

    // TODO: dedupe this func from GWallet.Frontend.XF.FrontendHelpers' IsDesktop() when this
    //       branch gets merged into master; note that we can't reuse IsDesktop()'s implementation
    //       here (inverted) as-is because it uses the Device.RuntimePlatform API, which doesn't
    //       come from Essentials but from the Xamarin.Forms package, which is only referenced by
    //       the GWallet.Frontend.XF project (and the master branch's Backend, which also uses
    //       DeviceInfo.Platform for platform checks, is in the same situation as ours)
    // NOTE: we use the DeviceInfo API from the DotNetEssentials nuget dependency (a fork of
    //       Xamarin.Essentials) for this: on mobile platforms, the platform-specific assemblies
    //       of that package (which the mobile frontends of the master branch reference) make its
    //       Platform property return the actual platform of the device, whereas on the rest of
    //       platforms (where its netstandard assembly gets used, like in this console frontend)
    //       it simply returns Unknown
    // NOTE: we reference this API fully-qualified (without opening the Xamarin.Essentials
    //       namespace) because opening it fails to compile in the legacy framework build, in
    //       the same way as the rest of usages in the master branch's Backend
    let IsMobilePlatform() =
        Xamarin.Essentials.DeviceInfo.Platform = Xamarin.Essentials.DevicePlatform.Android ||
        Xamarin.Essentials.DeviceInfo.Platform = Xamarin.Essentials.DevicePlatform.iOS

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

    // Workaround for https://github.com/nblockchain/geewallet/issues/312: .NET8 (or greater)
    // changed the folder that Environment.SpecialFolder.ApplicationData points at on macOS
    // (it used to be $HOME/.config, and it's now a subfolder of $HOME/Library), which meant
    // that geewallet stopped finding its config folder when its .NET runtime got upgraded
    // from .NET6 to .NET8. Windows and mobile platforms (Android & iOS) keep using the
    // ApplicationData folder (as before), but for the rest of platforms (e.g. macOS and
    // Linux) the config folder is now $HOME/.config/gwallet. If the new location doesn't
    // contain any existing config, we probe the folders that old geewallet versions could
    // have been using, and the first one that exists gets copied (migrated) to the new
    // location. The old folder is not deleted right away: that only happens once the
    // account balances have been retrieved from the new location and some of them are
    // positive (see MaybeRemoveMigratedOldConfigDir below).

    let private configDirResolutionLock = obj()

    // when a config dir migration happens, the old config dir is stored here so that it can
    // be removed later, once balances have been retrieved and some of them is positive
    let mutable private migratedOldConfigDir: Option<DirectoryInfo> = None

    let private HasExistingConfig (configDir: DirectoryInfo): bool =
        let accountsDir = DirectoryInfo(Path.Combine(configDir.FullName, "accounts"))
        accountsDir.Exists

    let rec private CopyDirRecursively (sourceDir: DirectoryInfo) (destinationDir: DirectoryInfo): unit =
        if not destinationDir.Exists then
            destinationDir.Create()
        for file in sourceDir.GetFiles() do
            file.CopyTo(Path.Combine(destinationDir.FullName, file.Name), true) |> ignore<FileInfo>
        for subDir in sourceDir.GetDirectories() do
            CopyDirRecursively subDir (DirectoryInfo(Path.Combine(destinationDir.FullName, subDir.Name)))

    // copies the first existing folder of oldConfigDirCandidates into newConfigDir (only if
    // the latter doesn't have an existing config), returning the old config dir that got
    // its contents migrated, if any
    let MaybeMigrateOldConfigDir (newConfigDir: DirectoryInfo)
                                 (oldConfigDirCandidates: seq<DirectoryInfo>): Option<DirectoryInfo> =
        if HasExistingConfig newConfigDir then
            None
        else
            let existingOldConfigDir =
                oldConfigDirCandidates
                |> Seq.filter (fun oldConfigDir -> oldConfigDir.Exists)
                |> Seq.tryHead
            match existingOldConfigDir with
            | Some oldConfigDir when oldConfigDir.FullName <> newConfigDir.FullName ->
                CopyDirRecursively oldConfigDir newConfigDir
                Some oldConfigDir
            | _ -> None

    let internal GetConfigDirForThisProgram(): DirectoryInfo =
        if IsWindowsPlatform() || IsMobilePlatform() then
            let configPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            let configDir = DirectoryInfo(Path.Combine(configPath, "gwallet"))
            if not configDir.Exists then
                configDir.Create()
            configDir
        else
            // the migration logic (and even the mere creation of the config dir) shouldn't
            // run several times in parallel, hence this lock
            lock configDirResolutionLock (fun _ ->
                let configPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                let configDir = DirectoryInfo(Path.Combine(configPath, ".config", "gwallet"))

                if not (HasExistingConfig configDir) then
                    let oldConfigDirCandidates = seq {
                        // the folder that old geewallet versions used on these platforms:
                        let appDataConfigPath =
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                        yield DirectoryInfo(Path.Combine(appDataConfigPath, "gwallet"))

                        // the folder that .NET8+ points at with ApplicationData on macOS:
                        yield DirectoryInfo(Path.Combine(configPath, "Library", "Application Support", "gwallet"))
                    }
                    migratedOldConfigDir <- MaybeMigrateOldConfigDir configDir oldConfigDirCandidates

                if not configDir.Exists then
                    configDir.Create()
                configDir
            )

    // to be called by frontends when account balances have been retrieved: if a config dir
    // migration happened, and some retrieved balance is positive, then the new config dir
    // location is confirmed to be working fine, and the old config dir can be removed
    let MaybeRemoveMigratedOldConfigDir (): unit =
        lock configDirResolutionLock (fun _ ->
            match migratedOldConfigDir with
            | Some oldConfigDir ->
                migratedOldConfigDir <- None
                if Directory.Exists oldConfigDir.FullName then
                    try
                        Directory.Delete(oldConfigDir.FullName, true)
                    with
                    | ex ->
                        Console.Error.WriteLine(
                            SPrintF2 "WARNING: old config folder (%s), which was migrated to the new config folder location, couldn't be removed; please remove it manually. The error was: %s"
                                oldConfigDir.FullName ex.Message)
            | _ -> ()
        )

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

    // In case a new token was added it will not have a config for an existing user
    // we copy the eth configs to the new tokens config directory
    let PropagateEthAccountInfoToMissingTokensAccounts() =
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

    let GetAccountFiles (currencies: seq<Currency>) (accountKind: AccountKind): seq<FileRepresentation> =
        seq {
            for currency in currencies do
                for filePath in Directory.GetFiles (GetConfigDir currency accountKind).FullName do
                    yield FileRepresentation.FromFile (FileInfo(filePath))
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
