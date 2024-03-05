#!/usr/bin/env -S dotnet fsi

open System
open System.IO
open System.Linq
open System.Diagnostics

#if !LEGACY_FRAMEWORK
#r "nuget: Fsdk, Version=0.6.0--date20230818-1152.git-83d671b"
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

type ProjectFile =
    | XFFrontend
    | GtkFrontend

let GetProject (projFile: ProjectFile) =
    let projFileName =
        match projFile with
        | GtkFrontend -> Path.Combine("GWallet.Frontend.XF.Gtk", "GWallet.Frontend.XF.Gtk.fsproj")
        | XFFrontend -> Path.Combine("GWallet.Frontend.XF", "GWallet.Frontend.XF.fsproj")

    let prjFile =
        Path.Combine("src", projFileName)
        |> FileInfo
    if not prjFile.Exists then
        raise <| FileNotFoundException("Project file not found", prjFile.FullName)
    prjFile

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

let NugetRestore (projectOrSolution: FileInfo) =
    let nugetArgs =
        sprintf
            "restore %s -DisableParallelProcessing -SolutionDirectory ."
            projectOrSolution.FullName
    let proc =
        Network.RunNugetCommand
            FsxHelper.NugetExe
            nugetArgs
            Echo.All
            false
    match proc.Result with
    | Error _ -> failwith "NuGet Restore failed ^"
    | _ -> ()

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

let BuildSolutionOrProject
    (buildToolAndBuildArg: string*string)
    (file: FileInfo)
    (binaryConfig: BinaryConfig)
    (maybeConstant: Option<string>)
    (extraOptions: string)
    =
#if LEGACY_FRAMEWORK
    NugetRestore file
#endif

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
                            file.FullName
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

let JustBuild binaryConfig maybeConstant: Frontend*FileInfo =
    let maybeBuildTool = Map.tryFind "BuildTool" buildConfigContents
    let maybeLegacyBuildTool = Map.tryFind "LegacyBuildTool" buildConfigContents

    let solutionFile = FsxHelper.GetSolution SolutionFile.Default
    let getBuildToolAndArgs(buildTool: string) =
        match buildTool with
        | "dotnet" ->
#if LEGACY_FRAMEWORK
            failwith "'dotnet' shouldn't be the build tool when using legacy framework, please report this bug"
#endif
            "dotnet", "build"
        | otherBuildTool ->
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
            otherBuildTool, String.Empty
#else
            otherBuildTool, String.Empty
#endif

    Console.WriteLine (sprintf "Building in %s mode..." (binaryConfig.ToString()))
    
    match maybeBuildTool, maybeLegacyBuildTool with
    | Some buildTool, _ 
    | None, Some buildTool ->
        BuildSolutionOrProject
            (getBuildToolAndArgs buildTool)
            solutionFile
            binaryConfig
            maybeConstant
            String.Empty
    | None, None ->
        failwith "A BuildTool or LegacyBuildTool should have been chosen by the configure script, please report this bug"

    let frontend =
        // older mono versions (which only have xbuild, not msbuild) can't compile .NET Standard assemblies
        match maybeBuildTool, maybeLegacyBuildTool with
        | _, Some legacyBuildTool when legacyBuildTool = "msbuild" ->

            let MSBuildRestoreAndBuild solutionFile =
                BuildSolutionOrProject (getBuildToolAndArgs legacyBuildTool) solutionFile binaryConfig maybeConstant "-target:Restore"
                // TODO: report as a bug the fact that /t:Restore;Build doesn't work while /t:Restore and later /t:Build does
                BuildSolutionOrProject (getBuildToolAndArgs legacyBuildTool) solutionFile binaryConfig maybeConstant "-target:Build"

            match Misc.GuessPlatform () with
            | Misc.Platform.Mac ->
                //this is because building in release requires code signing keys
                if binaryConfig = BinaryConfig.Debug then
                    let solution = FsxHelper.GetSolution SolutionFile.Mac
                    // somehow, msbuild doesn't restore the frontend dependencies (e.g. Xamarin.Forms) when targetting
                    // the {LINUX|MAC}_SOLUTION_FILE below, so we need this workaround. TODO: just finish migrating to MAUI(dotnet restore)
                    NugetRestore solution
                    MSBuildRestoreAndBuild solution

                Frontend.Console
            | Misc.Platform.Linux ->
                if FsxHelper.AreGtkLibsPresent Echo.All then
                    let solution = FsxHelper.GetSolution SolutionFile.Linux
                    // somehow, msbuild doesn't restore the frontend dependencies (e.g. Xamarin.Forms) when targetting
                    // the {LINUX|MAC}_SOLUTION_FILE below, so we need this workaround. TODO: just finish migrating to MAUI(dotnet restore)
                    NugetRestore solution
                    MSBuildRestoreAndBuild solution

                    Frontend.Gtk
                else
                    Frontend.Console

            | _ -> Frontend.Console
        | Some buildTool, Some legacyBuildTool when buildTool = "dotnet" && legacyBuildTool = "xbuild" ->
            if FsxHelper.AreGtkLibsPresent Echo.All then
                BuildSolutionOrProject
                    (getBuildToolAndArgs buildTool)
                    (GetProject ProjectFile.XFFrontend)
                    binaryConfig
                    maybeConstant
                    String.Empty

                let twoPhaseFlag = "/property:TwoPhaseBuildDueToXBuildUsage=true"

                let gtkFrontendProject = GetProject ProjectFile.GtkFrontend
                NugetRestore gtkFrontendProject
                BuildSolutionOrProject
                    (legacyBuildTool, twoPhaseFlag)
                    gtkFrontendProject
                    binaryConfig
                    maybeConstant
                    "/target:Build"

                Frontend.Gtk
            else
                Frontend.Console
        | _ -> Frontend.Console

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
#if LEGACY_FRAMEWORK
    if not FsxHelper.NugetExe.Exists then
        Network.DownloadNugetExe FsxHelper.NugetExe
#endif
    let buildConfig = BinaryConfig.Debug
    let frontend,_ = JustBuild buildConfig maybeConstant
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
        { Command = "dotnet"; Arguments = "--configuration Debug test " + testTarget.FullName }
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
    let fsxRunnerBin,fsxRunnerArg = FsxHelper.FsxRunnerInfo()
    Process.Execute(
        {
            Command = fsxRunnerBin
            Arguments = sprintf "%s %s" fsxRunnerArg sanityCheckScript
        },
        Echo.All
    ).UnwrapDefault() |> ignore<string>

| Some(someOtherTarget) ->
    Console.Error.WriteLine("Unrecognized target: " + someOtherTarget)
    Environment.Exit 2
