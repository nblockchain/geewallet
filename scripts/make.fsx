#!/usr/bin/env -S dotnet fsi

open System
open System.IO
open System.Linq
open System.Diagnostics

open System.Text
open System.Text.RegularExpressions
#r "System.Core.dll"
open System.Xml
#r "System.Xml.Linq.dll"
open System.Xml.Linq
open System.Xml.XPath

#if !LEGACY_FRAMEWORK
#r "nuget: Fsdk, Version=0.6.0--date20230812-0646.git-2268d50"
#else
#r "System.Configuration"
open System.Configuration
#load "fsx/Fsdk/Misc.fs"
#load "fsx/Fsdk/Process.fs"
#load "fsx/Fsdk/Network.fs"
#load "fsx/Fsdk/Git.fs"
#load "fsx/Fsdk/Unix.fs"
#endif
open Fsdk
open Fsdk.Process

#load "fsxHelper.fs"
open GWallet.Scripting

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

let buildConfigFileName = "build.config"
let buildConfigContents =
    let buildConfig =
        Path.Combine (FsxHelper.ScriptsDir.FullName, buildConfigFileName)
        |> FileInfo
    if not (buildConfig.Exists) then
        let configureLaunch =
            match Misc.GuessPlatform() with
            | Misc.Platform.Windows -> ".\\configure.bat"
            | _ -> "./configure.sh"
        Console.Error.WriteLine (sprintf "ERROR: configure hasn't been run yet, run %s first"
                                         configureLaunch)
        Environment.Exit 1

    let configFileLines = File.ReadAllLines buildConfig.FullName
    let skipBlankLines line = not <| String.IsNullOrWhiteSpace line
    let splitLineIntoKeyValueTuple (line:string) =
        let pair = line.Split([|'='|], StringSplitOptions.RemoveEmptyEntries)
        if pair.Length <> 2 then
            failwithf "All lines in '%s' must conform to key=value format, but got: '%s'. All lines: \n%s"
                      buildConfigFileName
                      line
                      (File.ReadAllText buildConfig.FullName)
        pair.[0], pair.[1]

    let buildConfigContents =
        configFileLines
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

let launcherScriptFile =
    Path.Combine (FsxHelper.ScriptsDir.FullName, "bin", UNIX_NAME)
    |> FileInfo
let mainBinariesDir binaryConfig =
    Path.Combine (
        FsxHelper.RootDir.FullName,
        "src",
        DEFAULT_FRONTEND,
        "bin",
        binaryConfig.ToString())
    |> DirectoryInfo

let wrapperScript = """#!/usr/bin/env bash
set -eo pipefail

if [[ $SNAP ]]; then
    PKG_DIR=$SNAP/usr
    export MONO_PATH=$PKG_DIR/lib/mono/4.5
    export MONO_CONFIG=$SNAP/etc/mono/config
    export MONO_CFG_DIR=$SNAP/etc
    export MONO_REGISTRY_PATH=~/.mono/registry
    export MONO_GAC_PREFIX=$PKG_DIR/lib/mono/gac/
fi

DIR_OF_THIS_SCRIPT=$(dirname "$(realpath "$0")")
FRONTEND_PATH="$DIR_OF_THIS_SCRIPT/../lib/$UNIX_NAME/$GWALLET_PROJECT.exe"
exec mono "$FRONTEND_PATH" "$@"
"""

#if LEGACY_FRAMEWORK
let PrintNugetVersion () =
    if not (FsxHelper.NugetExe.Exists) then
        false
    else
        let nugetProc =
            Network.RunNugetCommand
                FsxHelper.NugetExe
                String.Empty
                Echo.OutputOnly
                false
        match nugetProc.Result with
        | ProcessResultState.Success _ -> true
        | ProcessResultState.WarningsOrAmbiguous _output ->
            Console.WriteLine()
            Console.Out.Flush()

            failwith
                "nuget process succeeded but its output contained warnings ^"
        | ProcessResultState.Error(_exitCode, _output) ->
            Console.WriteLine()
            Console.Out.Flush()
            failwith "nuget process' output contained errors ^"
#endif

let BuildSolution
    (buildToolAndBuildArg: string*string)
    (solutionFileName: string)
    (binaryConfig: BinaryConfig)
    (maybeConstant: Option<string>)
    (extraOptions: string)
    =
    let buildTool,buildArg = buildToolAndBuildArg

    let configOption =
        if buildTool.StartsWith "dotnet" then
            sprintf "--configuration %s" (binaryConfig.ToString())
        else
            // TODO: use -property instead of /property when we don't need xbuild anymore
            sprintf "/property:Configuration=%s" (binaryConfig.ToString())

    let defineConstantsFromBuildConfig =
        match buildConfigContents |> Map.tryFind "DefineConstants" with
        | Some constants -> constants.Split([|";"|], StringSplitOptions.RemoveEmptyEntries) |> Seq.ofArray
        | None -> Seq.empty
    let defineConstantsSoFar =
        if not (buildTool.StartsWith "dotnet") then
            Seq.append ["LEGACY_FRAMEWORK"] defineConstantsFromBuildConfig
        else
            defineConstantsFromBuildConfig
    let allDefineConstants =
        match maybeConstant with
        | Some constant -> Seq.append [constant] defineConstantsSoFar
        | None -> defineConstantsSoFar
    let configOptions =
        if allDefineConstants.Any() then
            // FIXME: we shouldn't override the project's DefineConstants, but rather set "ExtraDefineConstants"
            // from the command line, and merge them later in the project file: see https://stackoverflow.com/a/32326853/544947
            let defineConstants =
                match binaryConfig with
                | Release -> allDefineConstants
                | Debug ->
                    if not (allDefineConstants.Contains "DEBUG") then
                        Seq.append allDefineConstants ["DEBUG"]
                    else
                        allDefineConstants

            let semiColon = ";"
            let semiColonEscaped = "%3B"
            match buildTool,Misc.GuessPlatform() with
            | "xbuild", _ ->
                // TODO: use -property instead of /property when we don't need xbuild anymore
                // xbuild: legacy of the legacy!
                // see https://github.com/dotnet/sdk/issues/9562
                sprintf "%s /property:DefineConstants=\"%s\"" configOption (String.Join(semiColonEscaped, defineConstants))
            | builtTool, Misc.Platform.Windows when buildTool.ToLower().Contains "msbuild" ->
                sprintf "%s -property:DefineConstants=\"%s\"" configOption (String.Join(semiColon, defineConstants))
            | _ ->
                sprintf "%s -property:DefineConstants=\\\"%s\\\"" configOption (String.Join(semiColon, defineConstants))
        else
            configOption
    let buildArgs = sprintf "%s %s %s %s"
                            buildArg
                            solutionFileName
                            configOptions
                            extraOptions
    let buildProcess = Process.Execute ({ Command = buildTool; Arguments = buildArgs }, Echo.All)
    match buildProcess.Result with
    | Error _ ->
        Console.WriteLine()
        Console.Error.WriteLine (sprintf "%s build failed" buildTool)
#if LEGACY_FRAMEWORK
        PrintNugetVersion() |> ignore
#endif
        Environment.Exit 1
    | _ -> ()

let JustBuild binaryConfig maybeConstant =
    let maybeBuildTool = Map.tryFind "BuildTool" buildConfigContents
    let mainSolution = "gwallet.sln"
    let buildTool,buildArg,solutionFileName =
        match maybeBuildTool with
        | None ->
            failwith "A BuildTool should have been chosen by the configure script, please report this bug"
        | Some "dotnet" ->
#if LEGACY_FRAMEWORK
            failwith "'dotnet' shouldn't be the build tool when using legacy framework, please report this bug"
#endif
            "dotnet", "build", mainSolution
        | Some otherBuildTool ->
#if LEGACY_FRAMEWORK
            let nugetConfig =
                Path.Combine(
                    FsxHelper.RootDir.FullName,
                    "NuGet.config")
                |> FileInfo
            let legacyNugetConfig =
                Path.Combine(
                    FsxHelper.RootDir.FullName,
                    "NuGet-legacy.config")
                |> FileInfo

            File.Copy(legacyNugetConfig.FullName, nugetConfig.FullName, true)
            otherBuildTool, String.Empty, "gwallet-legacy.sln"
#else
            otherBuildTool, String.Empty, mainSolution
#endif

    Console.WriteLine (sprintf "Building in %s mode..." (binaryConfig.ToString()))
    BuildSolution
        (buildTool, buildArg)
        solutionFileName
        binaryConfig
        maybeConstant
        String.Empty

    Directory.CreateDirectory(launcherScriptFile.Directory.FullName) |> ignore
    let wrapperScriptWithPaths =
        wrapperScript.Replace("$UNIX_NAME", UNIX_NAME)
                     .Replace("$GWALLET_PROJECT", DEFAULT_FRONTEND)
    File.WriteAllText (launcherScriptFile.FullName, wrapperScriptWithPaths)

let MakeCheckCommand (commandName: string) =
    if not (Process.CommandWorksInShell commandName) then
        Console.Error.WriteLine (sprintf "%s not found, please install it first" commandName)
        Environment.Exit 1

let GetPathToFrontendBinariesDir (binaryConfig: BinaryConfig) =
#if LEGACY_FRAMEWORK
    Path.Combine (FsxHelper.RootDir.FullName, "src", DEFAULT_FRONTEND, "bin", binaryConfig.ToString())
#else
    Path.Combine (FsxHelper.RootDir.FullName, "src", DEFAULT_FRONTEND, "bin", binaryConfig.ToString(), "net6.0")
#endif

let GetPathToBackend () =
    Path.Combine (FsxHelper.RootDir.FullName, "src", BACKEND)

let MakeAll (maybeConstant: Option<string>) =
    let buildConfig = BinaryConfig.Debug
    JustBuild buildConfig maybeConstant
    buildConfig

let RunFrontend (buildConfig: BinaryConfig) (maybeArgs: Option<string>) =
    let frontEndExtension =
#if LEGACY_FRAMEWORK
        ".exe"
#else
        ".dll"
#endif

    let pathToFrontend =
        Path.Combine(GetPathToFrontendBinariesDir buildConfig, DEFAULT_FRONTEND + frontEndExtension) |> FileInfo

#if LEGACY_FRAMEWORK
    match Misc.GuessPlatform() with
    | Misc.Platform.Windows -> ()
    | _ -> Unix.ChangeMode(pathToFrontend, "+x", false)
#endif

    let fileName, finalArgs =
        match maybeArgs with
#if LEGACY_FRAMEWORK
        | None | Some "" -> pathToFrontend.FullName, String.Empty
        | Some args -> pathToFrontend.FullName, args
#else
        | None | Some "" -> "dotnet", pathToFrontend.FullName
        | Some args -> "dotnet", (sprintf "%s %s" pathToFrontend.FullName args)
#endif

    let startInfo = ProcessStartInfo(FileName = fileName, Arguments = finalArgs, UseShellExecute = false)
    startInfo.EnvironmentVariables.["MONO_ENV_OPTIONS"] <- "--debug"

    let proc = Process.Start startInfo
    proc.WaitForExit()
    proc

let maybeTarget = GatherTarget (Misc.FsxOnlyArguments(), None)
match maybeTarget with
| None ->
    MakeAll None |> ignore

| Some("release") ->
    JustBuild BinaryConfig.Release None

#if LEGACY_FRAMEWORK
| Some "nuget" ->
    Console.WriteLine "This target is for debugging purposes."

    if not (PrintNugetVersion()) then
        Console.Error.WriteLine "Nuget executable has not been downloaded yet, try `make` alone first"
        Environment.Exit 1
#endif

| Some("zip") ->
    let zipCommand = "zip"
    MakeCheckCommand zipCommand

    let version = (Misc.GetCurrentVersion FsxHelper.RootDir).ToString()

    let release = BinaryConfig.Release
    JustBuild release None
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
    Process.Execute(
        {
            Command = "cp"
            Arguments =
                sprintf "-rfvp %s %s" pathToFrontend pathToFolderToBeZipped
        },
        Echo.All
    ).UnwrapDefault() |> ignore<string>
    let previousCurrentDir = Directory.GetCurrentDirectory()
    Directory.SetCurrentDirectory binDir
    let zipLaunch = { Command = zipCommand
                      Arguments = sprintf "%s -r %s %s"
                                      zipCommand zipName zipNameWithoutExtension }
    Process.Execute(zipLaunch, Echo.All).UnwrapDefault()
    |> ignore<string>
    Directory.SetCurrentDirectory previousCurrentDir

| Some("check") ->
    Console.WriteLine "Running tests..."
    Console.WriteLine ()

    let testProjectName = "GWallet.Backend.Tests"
#if !LEGACY_FRAMEWORK
    let testTarget =
        Path.Combine (
            FsxHelper.RootDir.FullName,
            "src",
            testProjectName,
            testProjectName + ".fsproj"
        ) |> FileInfo
#else
    // so that we get file names in stack traces
    Environment.SetEnvironmentVariable("MONO_ENV_OPTIONS", "--debug")

    let testTarget =
        Path.Combine (
            FsxHelper.RootDir.FullName,
            "src",
            testProjectName,
            "bin",
            testProjectName + ".dll"
        ) |> FileInfo
#endif

    if not testTarget.Exists then
        failwithf "File not found: %s" testTarget.FullName

    let runnerCommand =
#if !LEGACY_FRAMEWORK
        {
            Command = "dotnet"
            Arguments =
                sprintf "test %s --configuration Debug" testTarget.FullName
        }
#else
        match Misc.GuessPlatform() with
        | Misc.Platform.Linux ->
            let nunitCommand = "nunit-console"
            MakeCheckCommand nunitCommand

            { Command = nunitCommand; Arguments = testTarget.FullName }
        | _ ->
            if not FsxHelper.NugetExe.Exists then
                MakeAll None |> ignore

            let nunitVersion = "2.7.1"
            let pkgOutputDir = FsxHelper.NugetScriptsPackagesDir()
            Network.InstallNugetPackage
                FsxHelper.NugetExe
                pkgOutputDir
                "NUnit.Runners"
                (Some nunitVersion)
                Echo.All
            |> ignore

            {
                Command = Path.Combine(FsxHelper.NugetScriptsPackagesDir().FullName,
                                       sprintf "NUnit.Runners.%s" nunitVersion,
                                       "tools",
                                       "nunit-console.exe")
                Arguments = testTarget.FullName
            }
#endif

    let procResult = Process.Execute(runnerCommand, Echo.All)
#if !LEGACY_FRAMEWORK
    procResult.UnwrapDefault()
    |> ignore<string>
#else
    // in legacy mode, warnings (output to StdErr) happen even if exitCode=0
    match procResult.Result with
    | ProcessResultState.Error(_exitCode, _output) ->
        Console.WriteLine()
        Console.Out.Flush()

        failwith "Unit tests failed ^"
    | _ ->
        ()
#endif

| Some("install") ->
    let buildConfig = BinaryConfig.Release
    JustBuild buildConfig None

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
    Unix.ChangeMode(finalLauncherScriptInDestDir, "+x", false)

| Some("run") ->
    let buildConfig = MakeAll None
    RunFrontend buildConfig None
        |> ignore

| Some "update-servers" ->
    let buildConfig = MakeAll None
    Directory.SetCurrentDirectory (GetPathToBackend())
    let proc1 = RunFrontend buildConfig (Some "--update-servers-file")
    if proc1.ExitCode <> 0 then
        Environment.Exit proc1.ExitCode
    else
        let proc2 = RunFrontend buildConfig (Some "--update-servers-stats")
        Environment.Exit proc2.ExitCode

| Some "strict" ->
    MakeAll <| Some "STRICTER_COMPILATION_BUT_WITH_REFLECTION_AT_RUNTIME"
        |> ignore

| Some "sanitycheck" ->

#if LEGACY_FRAMEWORK
    if not FsxHelper.NugetExe.Exists then
        MakeAll None |> ignore

    let microsoftBuildLibVersion = "16.11.0"
    let pkgOutputDir = FsxHelper.NugetScriptsPackagesDir()
    Network.InstallNugetPackage
        FsxHelper.NugetExe
        pkgOutputDir
        "Microsoft.Build"
        (Some microsoftBuildLibVersion)
        Echo.All
    |> ignore

#endif

    let sanityCheckScript = Path.Combine(FsxHelper.ScriptsDir.FullName, "sanitycheck.fsx")
    Process.Execute(
        {
            Command = FsxHelper.FsxRunnerBin
            Arguments = sprintf "%s %s" FsxHelper.FsxRunnerArg sanityCheckScript
        },
        Echo.All
    ).UnwrapDefault() |> ignore<string>

| Some(someOtherTarget) ->
    Console.Error.WriteLine("Unrecognized target: " + someOtherTarget)
    Environment.Exit 2
