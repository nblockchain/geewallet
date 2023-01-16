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
#r "nuget: Fsdk"
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

let UNIX_NAME = "geewallet"
let CONSOLE_FRONTEND = "GWallet.Frontend.Console"
let GTK_FRONTEND = "GWallet.Frontend.XF.Gtk"
let DEFAULT_SOLUTION_FILE = "gwallet.core.sln"
let LINUX_SOLUTION_FILE = "gwallet.linux-legacy.sln"
let MAC_SOLUTION_FILE = "gwallet.mac-legacy.sln"
let MAUI_PROJECT_FILE = 
    Path.Combine("src", "GWallet.Frontend.Maui", "GWallet.Frontend.Maui.fsproj")
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
#if !LEGACY_FRAMEWORK
        sprintf "--configuration %s" (binaryConfig.ToString())
#else
        sprintf "/p:Configuration=%s" (binaryConfig.ToString())
#endif

    let defineConstantsFromBuildConfig =
        match buildConfigContents |> Map.tryFind "DefineConstants" with
        | Some constants -> constants.Split([|";"|], StringSplitOptions.RemoveEmptyEntries) |> Seq.ofArray
        | None -> Seq.empty
    let defineConstantsSoFar =
        if buildTool <> "dotnet" then
            Seq.append ["LEGACY_FRAMEWORK"] defineConstantsFromBuildConfig
        else
            defineConstantsFromBuildConfig
    let defineConstantsSoFar =
        if not(solutionFileName.EndsWith "maui.sln") then
            Seq.append ["XAMARIN"] defineConstantsSoFar
        else
            defineConstantsSoFar
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
                // xbuild: legacy of the legacy!
                // see https://github.com/dotnet/sdk/issues/9562
                sprintf "%s /p:DefineConstants=\"%s\"" configOption (String.Join(semiColonEscaped, defineConstants))
            | builtTool, Misc.Platform.Windows when buildTool.ToLower().Contains "msbuild" ->
                sprintf "%s /p:DefineConstants=\"%s\"" configOption (String.Join(semiColon, defineConstants))
            | _ ->
                sprintf "%s /p:DefineConstants=\\\"%s\\\"" configOption (String.Join(semiColon, defineConstants))
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

// TODO: we have to change this function to be the other way around (i.e. copy from Maui to XF) once we
//       have a finished version of Maui and we consider XF as legacy.
let CopyXamlFiles() = 
    let files = [| "WelcomePage.xaml" |]
    for file in files do
        let sourcePath = Path.Combine("src", "GWallet.Frontend.XF", file)
        let destPath = Path.Combine("src", "GWallet.Frontend.Maui", file)
            
        File.Copy(sourcePath, destPath, true)
        let fileText = File.ReadAllText(destPath)
        File.WriteAllText(
            destPath,
            fileText
                .Replace("http://xamarin.com/schemas/2014/forms","http://schemas.microsoft.com/dotnet/2021/maui")
                .Replace("GWallet.Frontend.XF", "GWallet.Frontend.Maui")
        )

        

let DotNetBuild
    (solutionProjectFileName: string)
    (binaryConfig: BinaryConfig)
    (args: string)
    (ignoreError: bool)
    =
    let configOption = sprintf "-c %s" (binaryConfig.ToString())
    let buildArgs = (sprintf "build %s %s %s" configOption solutionProjectFileName args)
    let buildProcess = Process.Execute ({ Command = "dotnet"; Arguments = buildArgs }, Echo.All)
    match buildProcess.Result with
    | Error _ ->
        if not ignoreError then
            Console.WriteLine()
            Console.Error.WriteLine "dotnet build failed"
#if LEGACY_FRAMEWORK
            PrintNugetVersion() |> ignore
#endif
            Environment.Exit 1
        else
            ()
    | _ -> ()

// We have to build Maui project for android twice because the first time we get
// an error about Resource file not found. The second time it works. 
// https://github.com/fabulous-dev/FSharp.Mobile.Templates/tree/55a1f3a0fd5cc397e48677ef4ff9241b360b0e84 
let BuildMauiProject binaryConfig =
    DotNetBuild MAUI_PROJECT_FILE binaryConfig "--framework net6.0-android" true
    DotNetBuild MAUI_PROJECT_FILE binaryConfig "--framework net6.0-android" false

let JustBuild binaryConfig maybeConstant: Frontend*FileInfo =
    let maybeBuildTool = Map.tryFind "BuildTool" buildConfigContents
    let mainSolution = DEFAULT_SOLUTION_FILE
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
            otherBuildTool, String.Empty, "gwallet.core-legacy.sln"
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

    let frontend =

        // older mono versions (which only have xbuild, not msbuild) can't compile .NET Standard assemblies
        if buildTool = "msbuild" then

#if LEGACY_FRAMEWORK
            // somehow, msbuild doesn't restore the frontend dependencies (e.g. Xamarin.Forms) when targetting
            // the {LINUX|MAC}_SOLUTION_FILE below, so we need this workaround. TODO: report this bug
            let ExplicitRestore projectOrSolutionRelativePath =
                let nugetWorkaroundArgs =
                    sprintf
                        "restore %s -SolutionDirectory ."
                        projectOrSolutionRelativePath
                Network.RunNugetCommand
                    FsxHelper.NugetExe
                    nugetWorkaroundArgs
                    Echo.All
                    true
                |> ignore
#endif

            let MSBuildRestoreAndBuild solutionFile =
                BuildSolution ("msbuild",buildArg) solutionFile binaryConfig maybeConstant "/t:Restore"
                // TODO: report as a bug the fact that /t:Restore;Build doesn't work while /t:Restore and later /t:Build does
                BuildSolution ("msbuild",buildArg) solutionFile binaryConfig maybeConstant "/t:Build"

            match Misc.GuessPlatform () with
            | Misc.Platform.Mac ->

                //this is because building in release requires code signing keys
                if binaryConfig = BinaryConfig.Debug then
                    let solution = MAC_SOLUTION_FILE
#if LEGACY_FRAMEWORK
                    ExplicitRestore solution
#endif
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
#if LEGACY_FRAMEWORK
                    ExplicitRestore solution
#endif
                    MSBuildRestoreAndBuild solution

                    Frontend.Gtk
                else
                    Frontend.Console

            | _ -> Frontend.Console
        elif buildTool = "dotnet" then
            match Misc.GuessPlatform () with
            | Misc.Platform.Mac ->
                if binaryConfig = BinaryConfig.Debug then
                    CopyXamlFiles()
                    BuildMauiProject binaryConfig
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
    let dir =
        Path.Combine(
            FsxHelper.RootDir.FullName,
            "src",
            frontendProjName,
            "bin",
            binaryConfig.ToString()
#if !LEGACY_FRAMEWORK
            , "net6.0"
#endif
        ) |> DirectoryInfo

    let frontEndExtension =
#if LEGACY_FRAMEWORK
        ".exe"
#else
        ".dll"
#endif

    let mainExecFile =
#if LEGACY_FRAMEWORK
        dir.GetFiles("*" + frontEndExtension, SearchOption.TopDirectoryOnly).Single()
#else
        // TODO: this might not work for 'make run' wrt Maui
        Path.Combine(dir.FullName, sprintf "%s%s" frontendProjName frontEndExtension)
        |> FileInfo
#endif

    dir,mainExecFile

let GetPathToBackend () =
    Path.Combine (FsxHelper.RootDir.FullName, "src", BACKEND)

let MakeAll (maybeConstant: Option<string>) =
    let buildConfig = BinaryConfig.Debug
    let frontend,_ = JustBuild buildConfig maybeConstant
    CopyXamlFiles()
    frontend,buildConfig

let RunFrontend (frontend: Frontend) (buildConfig: BinaryConfig) (maybeArgs: Option<string>) =

    let frontendDir,frontendExecutable = GetPathToFrontend frontend buildConfig
    let pathToFrontend = frontendExecutable.FullName

#if LEGACY_FRAMEWORK
    match Misc.GuessPlatform() with
    | Misc.Platform.Windows -> ()
    | _ -> Unix.ChangeMode(frontendExecutable, "+x", false)
#endif

    let fileName, finalArgs =
        match maybeArgs with
#if LEGACY_FRAMEWORK
        | None | Some "" -> pathToFrontend, String.Empty
        | Some args -> pathToFrontend, args
#else
        | None | Some "" -> "dotnet", pathToFrontend
        | Some args -> "dotnet", (sprintf "%s %s" pathToFrontend args)
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
        |> ignore

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
    Process.Execute(
        {
            Command = "cp"
            Arguments =
                sprintf "-rfvp %s %s" pathToFrontend.FullName pathToFolderToBeZipped
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
        { Command = "dotnet"; Arguments = "test " + testTarget.FullName }
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

    Process.Execute(runnerCommand, Echo.All).UnwrapDefault()
    |> ignore<string>

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
    Unix.ChangeMode(finalLauncherScriptInDestDir, "+x", false)

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
