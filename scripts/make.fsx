#!/usr/bin/env fsharpi

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

#r "System.Configuration"
open System.Configuration
#load "fsx/InfraLib/Misc.fs"
#load "fsx/InfraLib/Process.fs"
#load "fsx/InfraLib/Git.fs"
open FSX.Infrastructure
open Process

#load "fsxHelper.fs"
open GWallet.Scripting

let UNIX_NAME = "geewallet"
let CONSOLE_FRONTEND = "GWallet.Frontend.Console"
let GTK_FRONTEND = "GWallet.Frontend.XF.Gtk"
let DEFAULT_SOLUTION_FILE = "gwallet.core.sln"
let LINUX_SOLUTION_FILE = "gwallet.linux.sln"
let MAC_SOLUTION_FILE = "gwallet.mac.sln"
let BACKEND = "GWallet.Backend"

type Frontend =
    | Console
    | Gtk
    member self.GetProjectName() =
        match self with
        | Console -> CONSOLE_FRONTEND
        | Gtk -> GTK_FRONTEND
    member self.GetExecutableName() =
        match self with
        | Console -> CONSOLE_FRONTEND
        | Gtk -> UNIX_NAME
    override self.ToString() =
        sprintf "%A" self

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

let wrapperScript = """#!/usr/bin/env bash
set -eo pipefail

if [[ $SNAP ]]; then
    PKG_DIR=$SNAP/usr
    export MONO_PATH=$PKG_DIR/lib/mono/4.5:$PKG_DIR/lib/cli/gtk-sharp-2.0:$PKG_DIR/lib/cli/glib-sharp-2.0:$PKG_DIR/lib/cli/atk-sharp-2.0:$PKG_DIR/lib/cli/gdk-sharp-2.0:$PKG_DIR/lib/cli/pango-sharp-2.0:$MONO_PATH
    export MONO_CONFIG=$SNAP/etc/mono/config
    export MONO_CFG_DIR=$SNAP/etc
    export MONO_REGISTRY_PATH=~/.mono/registry
    export MONO_GAC_PREFIX=$PKG_DIR/lib/mono/gac/
fi

DIR_OF_THIS_SCRIPT=$(dirname "$(realpath "$0")")
FRONTEND_PATH="$DIR_OF_THIS_SCRIPT/../lib/$UNIX_NAME/$GWALLET_PROJECT.exe"
exec mono "$FRONTEND_PATH" "$@"
"""

let RunNugetCommand (command: string) echoMode (safe: bool) =
    let nugetCmd =
        match Misc.GuessPlatform() with
        | Misc.Platform.Linux ->
            { Command = "mono"; Arguments = sprintf "%s %s" FsxHelper.NugetExe.FullName command }
        | _ ->
            { Command = FsxHelper.NugetExe.FullName; Arguments = command }
    let proc = Process.Execute(nugetCmd, echoMode)

    if safe then
        proc.UnwrapDefault() |> ignore<string>

    proc

let PrintNugetVersion () =
    if not (FsxHelper.NugetExe.Exists) then
        false
    else
        let nugetProc = RunNugetCommand String.Empty Echo.OutputOnly false
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

let BuildSolution
    (buildTool: string)
    (solutionFileName: string)
    (binaryConfig: BinaryConfig)
    (maybeConstant: Option<string>)
    (extraOptions: string)
    =
    let configOption = sprintf "/p:Configuration=%s" (binaryConfig.ToString())
    let defineConstantsFromBuildConfig =
        match buildConfigContents |> Map.tryFind "DefineConstants" with
        | Some constants -> constants.Split([|";"|], StringSplitOptions.RemoveEmptyEntries) |> Seq.ofArray
        | None -> Seq.empty
    let allDefineConstants =
        match maybeConstant with
        | Some constant -> Seq.append [constant] defineConstantsFromBuildConfig
        | None -> defineConstantsFromBuildConfig

    let configOptions =
        if allDefineConstants.Any() then
            // FIXME: we shouldn't override the project's DefineConstants, but rather set "ExtraDefineConstants"
            // from the command line, and merge them later in the project file: see https://stackoverflow.com/a/32326853/544947
            sprintf "%s;DefineConstants=%s" configOption (String.Join(";", allDefineConstants))
        else
            configOption
    let buildArgs = sprintf "%s %s %s"
                            solutionFileName
                            configOptions
                            extraOptions
    let buildProcess = Process.Execute ({ Command = buildTool; Arguments = buildArgs }, Echo.All)
    match buildProcess.Result with
    | Error _ ->
        Console.WriteLine()
        Console.Error.WriteLine (sprintf "%s build failed" buildTool)
        PrintNugetVersion() |> ignore
        Environment.Exit 1
    | _ -> ()

let JustBuild binaryConfig maybeConstant: Frontend*FileInfo =
    let buildTool = Map.tryFind "BuildTool" buildConfigContents
    if buildTool.IsNone then
        failwith "A BuildTool should have been chosen by the configure script, please report this bug"

    Console.WriteLine (sprintf "Building in %s mode..." (binaryConfig.ToString()))
    BuildSolution
        buildTool.Value
        DEFAULT_SOLUTION_FILE
        binaryConfig
        maybeConstant
        String.Empty

    let frontend =

        // older mono versions (which only have xbuild, not msbuild) can't compile .NET Standard assemblies
        if buildTool.Value = "msbuild" then

            // somehow, msbuild doesn't restore the frontend dependencies (e.g. Xamarin.Forms) when targetting
            // the {LINUX|MAC}_SOLUTION_FILE below, so we need this workaround. TODO: report this bug
            let ExplicitRestore projectOrSolutionRelativePath =
                let nugetWorkaroundArgs =
                    sprintf
                        "%s restore %s -SolutionDirectory ."
                        FsxHelper.NugetExe.FullName projectOrSolutionRelativePath
                Process.Execute({ Command = "mono"; Arguments = nugetWorkaroundArgs }, Echo.All) |> ignore

            let MSBuildRestoreAndBuild solutionFile =
                BuildSolution "msbuild" solutionFile binaryConfig maybeConstant "/t:Restore"
                // TODO: report as a bug the fact that /t:Restore;Build doesn't work while /t:Restore and later /t:Build does
                BuildSolution "msbuild" solutionFile binaryConfig maybeConstant "/t:Build"

            match Misc.GuessPlatform () with
            | Misc.Platform.Mac ->

                //this is because building in release requires code signing keys
                if binaryConfig = BinaryConfig.Debug then
                    let solution = MAC_SOLUTION_FILE

                    ExplicitRestore solution

                    MSBuildRestoreAndBuild solution

                Frontend.Console
            | Misc.Platform.Linux ->
                let pkgConfigForGtkProc = Process.Execute({ Command = "pkg-config"; Arguments = "gtk-sharp-2.0" }, Echo.All)
                let isGtkPresent =
                    match pkgConfigForGtkProc.Result with
                    | Error _ -> false
                    | _ -> true

                if isGtkPresent then
                    let solution = LINUX_SOLUTION_FILE

                    ExplicitRestore solution

                    MSBuildRestoreAndBuild solution

                    Frontend.Gtk
                else
                    Frontend.Console

            | _ -> Frontend.Console

        else
            Frontend.Console

    let scriptName = sprintf "%s-%s" UNIX_NAME (frontend.ToString().ToLower())
    let launcherScriptFile =
        Path.Combine (FsxHelper.ScriptsDir.FullName, "bin", scriptName)
        |> FileInfo
    Directory.CreateDirectory(launcherScriptFile.Directory.FullName) |> ignore
    let wrapperScriptWithPaths =
        wrapperScript.Replace("$UNIX_NAME", UNIX_NAME)
                     .Replace("$GWALLET_PROJECT", frontend.GetExecutableName())
    File.WriteAllText (launcherScriptFile.FullName, wrapperScriptWithPaths)
    frontend,launcherScriptFile

let MakeCheckCommand (commandName: string) =
    if not (Process.CommandWorksInShell commandName) then
        Console.Error.WriteLine (sprintf "%s not found, please install it first" commandName)
        Environment.Exit 1

let GetPathToFrontend (frontend: Frontend) (binaryConfig: BinaryConfig): DirectoryInfo*FileInfo =
    let frontendProjName = frontend.GetProjectName()
    let dir = Path.Combine (FsxHelper.RootDir.FullName, "src", frontendProjName, "bin", binaryConfig.ToString())
                  |> DirectoryInfo
    let mainExecFile = dir.GetFiles("*.exe", SearchOption.TopDirectoryOnly).Single()
    dir,mainExecFile

let GetPathToBackend () =
    Path.Combine (FsxHelper.RootDir.FullName, "src", BACKEND)

let MakeAll (maybeConstant: Option<string>) =
    let buildConfig = BinaryConfig.Debug
    let frontend,_ = JustBuild buildConfig maybeConstant
    frontend,buildConfig

let RunFrontend (frontend: Frontend) (buildConfig: BinaryConfig) (maybeArgs: Option<string>) =
    let monoVersion = Map.tryFind "MonoPkgConfigVersion" buildConfigContents

    let frontendDir,frontendExecutable = GetPathToFrontend frontend buildConfig
    let pathToFrontend = frontendExecutable.FullName

    let fileName, finalArgs =
        match maybeArgs with
        | None | Some "" -> pathToFrontend,String.Empty
        | Some args -> pathToFrontend,args

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
        |> ignore

| Some "nuget" ->
    Console.WriteLine "This target is for debugging purposes."

    if not (PrintNugetVersion()) then
        Console.Error.WriteLine "Nuget executable has not been downloaded yet, try `make` alone first"
        Environment.Exit 1

| Some("zip") ->
    let zipCommand = "zip"
    MakeCheckCommand zipCommand

    let version = (Misc.GetCurrentVersion FsxHelper.RootDir).ToString()

    let release = BinaryConfig.Release
    let frontend,script = JustBuild release None
    let binDir = "bin"
    Directory.CreateDirectory(binDir) |> ignore

    let zipNameWithoutExtension = sprintf "%s-v%s" script.Name version
    let zipName = sprintf "%s.zip" zipNameWithoutExtension
    let pathToZip = Path.Combine(binDir, zipName)
    if (File.Exists (pathToZip)) then
        File.Delete (pathToZip)

    let pathToFolderToBeZipped = Path.Combine(binDir, zipNameWithoutExtension)
    if (Directory.Exists (pathToFolderToBeZipped)) then
        Directory.Delete (pathToFolderToBeZipped, true)

    let pathToFrontend,_ = GetPathToFrontend frontend release
    let cpRun = Process.Execute({ Command = "cp"
                                  Arguments = sprintf "-rfvp %s %s" pathToFrontend.FullName pathToFolderToBeZipped },
                                Echo.All)
    match cpRun.Result with
    | Error _ ->
        Console.WriteLine()
        Console.Error.WriteLine "Precopy for ZIP compression failed"
        Environment.Exit 1
    | _ -> ()

    let previousCurrentDir = Directory.GetCurrentDirectory()
    Directory.SetCurrentDirectory binDir
    let zipLaunch = { Command = zipCommand
                      Arguments = sprintf "%s -r %s %s"
                                      zipCommand zipName zipNameWithoutExtension }
    let zipRun = Process.Execute(zipLaunch, Echo.All)
    match zipRun.Result with
    | Error _ ->
        Console.WriteLine()
        Console.Error.WriteLine "ZIP compression failed"
        Environment.Exit 1
    | _ -> ()
    Directory.SetCurrentDirectory previousCurrentDir

| Some("check") ->
    Console.WriteLine "Running tests..."
    Console.WriteLine ()

    // so that we get file names in stack traces
    Environment.SetEnvironmentVariable("MONO_ENV_OPTIONS", "--debug")

    let testAssemblyName = "GWallet.Backend.Tests"
    let testAssembly =
        Path.Combine (
            FsxHelper.RootDir.FullName,
            "src",
            testAssemblyName,
            "bin",
            testAssemblyName + ".dll"
        ) |> FileInfo
    if not testAssembly.Exists then
        failwithf "File not found: %s" testAssembly.FullName

    let runnerCommand =
        match Misc.GuessPlatform() with
        | Misc.Platform.Linux ->
            let nunitCommand = "nunit-console"
            MakeCheckCommand nunitCommand

            { Command = nunitCommand; Arguments = testAssembly.FullName }
        | _ ->
            if not FsxHelper.NugetExe.Exists then
                MakeAll None |> ignore

            let nunitVersion = "2.7.1"
            let installNUnitRunnerNugetCommand =
                sprintf
                    "install NUnit.Runners -Version %s -OutputDirectory %s"
                    nunitVersion (FsxHelper.NugetScriptsPackagesDir().FullName)
            RunNugetCommand installNUnitRunnerNugetCommand Echo.All true
                |> ignore

            {
                Command = Path.Combine(FsxHelper.NugetScriptsPackagesDir().FullName,
                                       sprintf "NUnit.Runners.%s" nunitVersion,
                                       "tools",
                                       "nunit-console.exe")
                Arguments = testAssembly.FullName
            }

    let nunitRun = Process.Execute(runnerCommand,
                                   Echo.All)
    match nunitRun.Result with
    | Error _ ->
        Console.WriteLine()
        Console.Error.WriteLine "Tests failed"
        Environment.Exit 1
    | _ -> ()

| Some("install") ->
    let buildConfig = BinaryConfig.Release
    let frontend,launcherScriptFile = JustBuild buildConfig None

    let mainBinariesDir binaryConfig = DirectoryInfo (Path.Combine(FsxHelper.RootDir.FullName,
                                                                   "src",
                                                                   frontend.GetProjectName(),
                                                                   "bin",
                                                                   binaryConfig.ToString()))


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
    Process.Execute(
        {
            Command = "chmod"
            Arguments =
                sprintf "ugo+x %s" finalLauncherScriptInDestDir.FullName
        },
        Echo.Off
    ).Unwrap("Unexpected chmod failure, please report this bug") |> ignore<string>

| Some("run") ->
    let frontend,buildConfig = MakeAll None
    RunFrontend frontend buildConfig None
        |> ignore

| Some "update-servers" ->
    let _,buildConfig = MakeAll None
    Directory.SetCurrentDirectory (GetPathToBackend())
    let proc1 = RunFrontend Frontend.Console buildConfig (Some "--update-servers-file")
    if proc1.ExitCode <> 0 then
        Environment.Exit proc1.ExitCode
    else
        let proc2 = RunFrontend Frontend.Console buildConfig (Some "--update-servers-stats")
        Environment.Exit proc2.ExitCode

| Some "strict" ->
    MakeAll <| Some "STRICTER_COMPILATION_BUT_WITH_REFLECTION_AT_RUNTIME"
        |> ignore

| Some "sanitycheck" ->

    if not FsxHelper.NugetExe.Exists then
        MakeAll None |> ignore

    let microsoftBuildLibVersion = "16.11.0"
    let installMicrosoftBuildLibRunnerNugetCommand =
        sprintf
            "install Microsoft.Build -Version %s -OutputDirectory %s"
            microsoftBuildLibVersion (FsxHelper.NugetScriptsPackagesDir().FullName)
    RunNugetCommand installMicrosoftBuildLibRunnerNugetCommand Echo.All true
        |> ignore

    let sanityCheckScript = Path.Combine(FsxHelper.ScriptsDir.FullName, "sanitycheck.fsx")
    Process.Execute(
        {
            Command = FsxHelper.FsxRunner
            Arguments = sanityCheckScript
        },
        Echo.All
    ).UnwrapDefault() |> ignore<string>

| Some(someOtherTarget) ->
    Console.Error.WriteLine("Unrecognized target: " + someOtherTarget)
    Environment.Exit 2
