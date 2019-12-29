#!/usr/bin/env fsharpi

open System
open System.IO
open System.Diagnostics

#r "System.Configuration"
#load "InfraLib/Misc.fs"
#load "InfraLib/Process.fs"
#load "InfraLib/Git.fs"
open FSX.Infrastructure
open Process

let UNIX_NAME = "gwallet"
let DEFAULT_FRONTEND = "GWallet.Frontend.Console"
let BACKEND = "GWallet.Backend"

type BinaryConfig =
    | Debug
    | Release
    override self.ToString() =
        sprintf "%A" self

let rec private GatherTarget (args: string list, targetSet: Option<string>): Option<string> =
    match args with
    | [] -> targetSet
    | head::tail ->
        if (targetSet.IsSome) then
            failwith "only one target can be passed to make"
        GatherTarget (tail, Some (head))

let scriptsDir = __SOURCE_DIRECTORY__ |> DirectoryInfo
let rootDir = Path.Combine(scriptsDir.FullName, "..") |> DirectoryInfo

let buildConfigFileName = "build.config"
let buildConfigContents =
    let buildConfig = FileInfo (Path.Combine (scriptsDir.FullName, buildConfigFileName))
    if not (buildConfig.Exists) then
        let configureLaunch =
            match Misc.GuessPlatform() with
            | Misc.Platform.Windows -> ".\\configure.bat"
            | _ -> "./configure.sh"
        Console.Error.WriteLine (sprintf "ERROR: configure hasn't been run yet, run %s first"
                                         configureLaunch)
        Environment.Exit 1

    let skipBlankLines line = not <| String.IsNullOrWhiteSpace line
    let splitLineIntoKeyValueTuple (line:string) =
        let pair = line.Split([|'='|], StringSplitOptions.RemoveEmptyEntries)
        if pair.Length <> 2 then
            failwithf "All lines in %s must conform to format:\n\tkey=value"
                      buildConfigFileName
        pair.[0], pair.[1]

    let buildConfigContents =
        File.ReadAllLines buildConfig.FullName
        |> Array.filter skipBlankLines
        |> Array.map splitLineIntoKeyValueTuple
        |> Map.ofArray
    buildConfigContents

let GetOrExplain key map =
    match map |> Map.tryFind key with
    | Some k -> k
    | None   -> failwithf "No entry exists in %s with a key '%s'."
                          buildConfigFileName key

let prefix = buildConfigContents |> GetOrExplain "Prefix"
let libPrefixDir = DirectoryInfo (Path.Combine (prefix, "lib", UNIX_NAME))
let binPrefixDir = DirectoryInfo (Path.Combine (prefix, "bin"))

let launcherScriptFile = Path.Combine (scriptsDir.FullName, "bin", UNIX_NAME) |> FileInfo
let mainBinariesDir binaryConfig = DirectoryInfo (Path.Combine(rootDir.FullName,
                                                               "src",
                                                               DEFAULT_FRONTEND,
                                                               "bin",
                                                               binaryConfig.ToString()))

let wrapperScript = """#!/usr/bin/env bash
set -euo pipefail

exec mono "$TARGET_DIR/$GWALLET_PROJECT.exe" "$@"
"""

let nugetExe = Path.Combine(rootDir.FullName, ".nuget", "nuget.exe") |> FileInfo
let nugetPackagesSubDirName = "packages"

let PrintNugetVersion () =
    if not (nugetExe.Exists) then
        false
    else
        let nugetProc = Process.Execute ({ Command = "mono"; Arguments = nugetExe.FullName }, Echo.Off)
        Console.WriteLine nugetProc.Output.StdOut
        if nugetProc.ExitCode = 0 then
            true
        else
            Console.Error.WriteLine nugetProc.Output.StdErr
            Console.WriteLine()
            failwith "nuget process' output contained errors ^"

let JustBuild binaryConfig =
    let buildTool = Map.tryFind "BuildTool" buildConfigContents
    if buildTool.IsNone then
        failwith "A BuildTool should have been chosen by the configure script, please report this bug"

    Console.WriteLine (sprintf "Building in %s mode..." (binaryConfig.ToString()))
    let configOption = sprintf "/p:Configuration=%s" (binaryConfig.ToString())
    let configOptions =
        match buildConfigContents |> Map.tryFind "DefineConstants" with
        | Some constants -> sprintf "%s;DefineConstants=%s" configOption constants
        | None   -> configOption
    let buildProcess = Process.Execute ({ Command = buildTool.Value; Arguments = configOptions }, Echo.All)
    if (buildProcess.ExitCode <> 0) then
        Console.Error.WriteLine (sprintf "%s build failed" buildTool.Value)
        PrintNugetVersion() |> ignore
        Environment.Exit 1

    Directory.CreateDirectory(launcherScriptFile.Directory.FullName) |> ignore
    let wrapperScriptWithPaths =
        wrapperScript.Replace("$TARGET_DIR", libPrefixDir.FullName)
                     .Replace("$GWALLET_PROJECT", DEFAULT_FRONTEND)
    File.WriteAllText (launcherScriptFile.FullName, wrapperScriptWithPaths)

let MakeCheckCommand (commandName: string) =
    if not (Process.CommandWorksInShell commandName) then
        Console.Error.WriteLine (sprintf "%s not found, please install it first" commandName)
        Environment.Exit 1

let GetPathToFrontendBinariesDir (binaryConfig: BinaryConfig) =
    Path.Combine (rootDir.FullName, "src", DEFAULT_FRONTEND, "bin", binaryConfig.ToString())

let GetPathToBackend () =
    Path.Combine (rootDir.FullName, "src", BACKEND)

let MakeAll() =
    let buildConfig = BinaryConfig.Debug
    JustBuild buildConfig
    buildConfig

let RunFrontend (buildConfig: BinaryConfig) (maybeArgs: Option<string>) =
    let oldVersionOfMono =
        let versionOfMonoWhereRunningExesDirectlyIsSupported = "5.16"

        match Misc.GuessPlatform() with
        | Misc.Platform.Windows ->
            // not using Mono anyway
            false
        | Misc.Platform.Mac ->
            // unlikely that anyone uses old Mono versions in Mac, as it's easy to update (TODO: detect anyway)
            false
        | Misc.Platform.Linux ->
            let pkgConfig = "pkg-config"
            if not (Process.CommandWorksInShell pkgConfig) then
                failwithf "'%s' was uninstalled after ./configure.sh was invoked?" pkgConfig
            let pkgConfigCmd = { Command = pkgConfig
                                 Arguments = sprintf "--atleast-version=%s mono"
                                                 versionOfMonoWhereRunningExesDirectlyIsSupported }
            let processResult = Process.Execute(pkgConfigCmd, Echo.OutputOnly)
            processResult.ExitCode <> 0

    let pathToFrontend = Path.Combine(GetPathToFrontendBinariesDir buildConfig, DEFAULT_FRONTEND + ".exe")

    let fileName, finalArgs =
        if oldVersionOfMono then
            match maybeArgs with
            | None | Some "" -> "mono",pathToFrontend
            | Some args -> "mono",pathToFrontend + " " + args
        else
            match maybeArgs with
            | None | Some "" -> pathToFrontend,String.Empty
            | Some args -> pathToFrontend,args

    let startInfo = ProcessStartInfo(FileName = fileName, Arguments = finalArgs, UseShellExecute = false)
    startInfo.EnvironmentVariables.["MONO_ENV_OPTIONS"] <- "--debug"

    let proc = Process.Start startInfo
    proc.WaitForExit()
    proc

let maybeTarget = GatherTarget (Misc.FsxArguments(), None)
match maybeTarget with
| None ->
    MakeAll() |> ignore

| Some("release") ->
    JustBuild BinaryConfig.Release

| Some "nuget" ->
    Console.WriteLine "This target is for debugging purposes."

    if not (PrintNugetVersion()) then
        Console.Error.WriteLine "Nuget executable has not been downloaded yet, try `make` alone first"
        Environment.Exit 1

| Some("zip") ->
    let zipCommand = "zip"
    MakeCheckCommand zipCommand

    let version = Misc.GetCurrentVersion(rootDir).ToString()

    let release = BinaryConfig.Release
    JustBuild release
    let binDir = "bin"
    Directory.CreateDirectory(binDir) |> ignore

    let zipNameWithoutExtension = sprintf "%s.v.%s" UNIX_NAME version
    let zipName = sprintf "%s.zip" zipNameWithoutExtension
    let pathToZip = Path.Combine(binDir, zipName)
    if (File.Exists (pathToZip)) then
        File.Delete (pathToZip)

    let pathToFolderToBeZipped = Path.Combine(binDir, zipNameWithoutExtension)
    if (Directory.Exists (pathToFolderToBeZipped)) then
        Directory.Delete (pathToFolderToBeZipped, true)

    let pathToFrontend = GetPathToFrontendBinariesDir release
    let zipRun = Process.Execute({ Command = "cp"
                                   Arguments = sprintf "-rfvp %s %s" pathToFrontend pathToFolderToBeZipped },
                                 Echo.All)
    if (zipRun.ExitCode <> 0) then
        Console.Error.WriteLine "Precopy for ZIP compression failed"
        Environment.Exit 1

    let previousCurrentDir = Directory.GetCurrentDirectory()
    Directory.SetCurrentDirectory binDir
    let zipLaunch = { Command = zipCommand
                      Arguments = sprintf "%s -r %s %s"
                                      zipCommand zipName zipNameWithoutExtension }
    let zipRun = Process.Execute(zipLaunch, Echo.All)
    if (zipRun.ExitCode <> 0) then
        Console.Error.WriteLine "ZIP compression failed"
        Environment.Exit 1
    Directory.SetCurrentDirectory previousCurrentDir

| Some("check") ->
    Console.WriteLine "Running tests..."
    Console.WriteLine ()

    let testAssemblyName = "GWallet.Backend.Tests"
    let testAssembly = Path.Combine(rootDir.FullName, "src", testAssemblyName, "bin",
                                    testAssemblyName + ".dll") |> FileInfo
    if not testAssembly.Exists then
        failwithf "File not found: %s" testAssembly.FullName

    let runnerCommand =
        match Misc.GuessPlatform() with
        | Misc.Platform.Linux ->
            let nunitCommand = "nunit-console"
            MakeCheckCommand nunitCommand

            { Command = nunitCommand; Arguments = testAssembly.FullName }
        | _ ->
            let nunitVersion = "2.7.1"
            if not nugetExe.Exists then
                MakeAll () |> ignore

            let nugetInstallCommand =
                {
                    Command = nugetExe.FullName
                    Arguments = sprintf "install NUnit.Runners -Version %s -OutputDirectory %s"
                                        nunitVersion nugetPackagesSubDirName
                }
            Process.SafeExecute(nugetInstallCommand, Echo.All)
                |> ignore

            {
                Command = Path.Combine(nugetPackagesSubDirName,
                                       sprintf "NUnit.Runners.%s" nunitVersion,
                                       "tools",
                                       "nunit-console.exe")
                Arguments = testAssembly.FullName
            }

    let nunitRun = Process.Execute(runnerCommand,
                                   Echo.All)
    if (nunitRun.ExitCode <> 0) then
        Console.Error.WriteLine "Tests failed"
        Environment.Exit 1

| Some("install") ->
    let buildConfig = BinaryConfig.Release
    JustBuild buildConfig

    let destDirUpperCase = Environment.GetEnvironmentVariable "DESTDIR"
    let destDirLowerCase = Environment.GetEnvironmentVariable "DestDir"
    let destDir =
        if not (String.IsNullOrEmpty destDirUpperCase) then
            destDirUpperCase |> DirectoryInfo
        elif not (String.IsNullOrEmpty destDirLowerCase) then
            destDirLowerCase |> DirectoryInfo
        else
            prefix |> DirectoryInfo

    let libDestDir = Path.Combine(destDir.FullName, "lib", UNIX_NAME) |> DirectoryInfo
    let binDestDir = Path.Combine(destDir.FullName, "bin") |> DirectoryInfo

    Console.WriteLine "Installing..."
    Console.WriteLine ()
    Misc.CopyDirectoryRecursively (mainBinariesDir buildConfig, libDestDir, [])

    let finalLauncherScriptInDestDir = Path.Combine(binDestDir.FullName, launcherScriptFile.Name) |> FileInfo
    if not (Directory.Exists(finalLauncherScriptInDestDir.Directory.FullName)) then
        Directory.CreateDirectory(finalLauncherScriptInDestDir.Directory.FullName) |> ignore
    File.Copy(launcherScriptFile.FullName, finalLauncherScriptInDestDir.FullName, true)
    if Process.Execute({ Command = "chmod"; Arguments = sprintf "ugo+x %s" finalLauncherScriptInDestDir.FullName },
                        Echo.Off).ExitCode <> 0 then
        failwith "Unexpected chmod failure, please report this bug"

| Some("run") ->
    let buildConfig = MakeAll()
    RunFrontend buildConfig None
        |> ignore

| Some "update-servers" ->
    let buildConfig = MakeAll()
    Directory.SetCurrentDirectory (GetPathToBackend())
    let proc1 = RunFrontend buildConfig (Some "--update-servers-file")
    if proc1.ExitCode <> 0 then
        Environment.Exit proc1.ExitCode
    else
        let proc2 = RunFrontend buildConfig (Some "--update-servers-stats")
        Environment.Exit proc2.ExitCode

| Some(someOtherTarget) ->
    Console.Error.WriteLine("Unrecognized target: " + someOtherTarget)
    Environment.Exit 2
