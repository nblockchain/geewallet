#!/usr/bin/env fsharpi

open System
open System.IO
open System.Linq
open System.Diagnostics

#r "System.Configuration"
open System.Configuration
#load "InfraLib/Misc.fs"
#load "InfraLib/Process.fs"
#load "InfraLib/Git.fs"
open FSX.Infrastructure
open Process

let UNIX_NAME = "geewallet"
let CONSOLE_FRONTEND = "GWallet.Frontend.Console"
let GTK_FRONTEND = "GWallet.Frontend.XF.Gtk"
let DEFAULT_SOLUTION_FILE = "gwallet.core.sln"
let LINUX_SOLUTION_FILE = "gwallet.linux.sln"
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

let nugetExe = Path.Combine(rootDir.FullName, ".nuget", "nuget.exe") |> FileInfo
let nugetPackagesSubDirName = "packages"

let PrintNugetVersion () =
    if not (nugetExe.Exists) then
        false
    else
        let nugetCmd =
            match Misc.GuessPlatform() with
            | Misc.Platform.Windows ->
                { Command = nugetExe.FullName; Arguments = String.Empty }
            | _ -> { Command = "mono"; Arguments = nugetExe.FullName }
        let nugetProc = Process.Execute (nugetCmd, Echo.Off)
        Console.WriteLine nugetProc.Output.StdOut
        if nugetProc.ExitCode = 0 then
            true
        else
            Console.Error.WriteLine nugetProc.Output.StdErr
            Console.WriteLine()
            failwith "nuget process' output contained errors ^"

let BuildSolution buildTool solutionFileName binaryConfig maybeConstant extraOptions =
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
    if (buildProcess.ExitCode <> 0) then
        Console.Error.WriteLine (sprintf "%s build failed" buildTool)
        PrintNugetVersion() |> ignore
        Environment.Exit 1

let JustBuild binaryConfig maybeConstant: Frontend*FileInfo =
    printfn "Building in %s mode..." (binaryConfig.ToString().ToUpper())
    let buildTool = Map.tryFind "BuildTool" buildConfigContents
    if buildTool.IsNone then
        failwith "A BuildTool should have been chosen by the configure script, please report this bug"

    BuildSolution buildTool.Value DEFAULT_SOLUTION_FILE binaryConfig maybeConstant String.Empty

    let frontend =
        // older mono versions (which only have xbuild, not msbuild) can't compile .NET Standard assemblies
        if buildTool.Value = "msbuild" && Misc.GuessPlatform () = Misc.Platform.Linux then

            let pkgConfigForGtkProc = Process.Execute({ Command = "pkg-config"; Arguments = "gtk-sharp-2.0" }, Echo.All)
            let isGtkPresent =
                (0 = pkgConfigForGtkProc.ExitCode)

            if isGtkPresent then

                // somehow, msbuild doesn't restore the dependencies of the GTK frontend (Xamarin.Forms in particular)
                // when targetting the LINUX_SOLUTION_FILE below, so we need this workaround. TODO: report this bug
                let nugetWorkaroundArgs =
                    sprintf "%s restore src/%s/%s.fsproj -SolutionDirectory ."
                            nugetExe.FullName GTK_FRONTEND GTK_FRONTEND
                Process.Execute({ Command = "mono"; Arguments = nugetWorkaroundArgs }, Echo.All) |> ignore

                BuildSolution "msbuild" LINUX_SOLUTION_FILE binaryConfig maybeConstant "/t:Restore"
                // TODO: report as a bug the fact that /t:Restore;Build doesn't work while /t:Restore and later /t:Build does
                BuildSolution "msbuild" LINUX_SOLUTION_FILE binaryConfig maybeConstant "/t:Build"
                Frontend.Gtk
            else
                Frontend.Console
        else
            Frontend.Console

    let scriptName = sprintf "%s-%s" UNIX_NAME (frontend.ToString().ToLower())
    let launcherScriptFile = Path.Combine (scriptsDir.FullName, "bin", scriptName) |> FileInfo
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
    let dir = Path.Combine (rootDir.FullName, "src", frontendProjName, "bin", binaryConfig.ToString())
                  |> DirectoryInfo
    let mainExecFile = dir.GetFiles("*.exe", SearchOption.TopDirectoryOnly).Single()
    dir,mainExecFile

let GetPathToBackend () =
    Path.Combine (rootDir.FullName, "src", BACKEND)

let MakeAll maybeConstant =
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

let maybeTarget = GatherTarget (Misc.FsxArguments(), None)
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

    let version = Misc.GetCurrentVersion(rootDir).ToString()

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
    let zipRun = Process.Execute({ Command = "cp"
                                   Arguments = sprintf "-rfvp %s %s" pathToFrontend.FullName pathToFolderToBeZipped },
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

    // so that we get file names in stack traces
    Environment.SetEnvironmentVariable("MONO_ENV_OPTIONS", "--debug")

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
                MakeAll None |> ignore

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
    let frontend,launcherScriptFile = JustBuild buildConfig None

    let mainBinariesDir binaryConfig = DirectoryInfo (Path.Combine(rootDir.FullName,
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
    if Process.Execute({ Command = "chmod"; Arguments = sprintf "ugo+x %s" finalLauncherScriptInDestDir.FullName },
                        Echo.Off).ExitCode <> 0 then
        failwith "Unexpected chmod failure, please report this bug"

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
    let FindOffendingPrintfUsage () =
        let findScript = Path.Combine(rootDir.FullName, "scripts", "find.fsx")
        let fsxRunner =
            match Misc.GuessPlatform() with
            | Misc.Platform.Windows ->
                Path.Combine(rootDir.FullName, "scripts", "fsi.bat")
            | _ ->
                let fsxRunnerEnvVar = Environment.GetEnvironmentVariable "FsxRunner"
                if String.IsNullOrEmpty fsxRunnerEnvVar then
                    failwith "FsxRunner env var should have been passed to make.sh"
                fsxRunnerEnvVar
        let excludeFolders =
            String.Format("scripts{0}" +
                          "src{1}GWallet.Frontend.Console{0}" +
                          "src{1}GWallet.Backend.Tests{0}" +
                          "src{1}GWallet.Backend{1}FSharpUtil.fs",
                          Path.PathSeparator, Path.DirectorySeparatorChar)

        let proc =
            {
                Command = fsxRunner
                Arguments = sprintf "%s --exclude=%s %s"
                                    findScript
                                    excludeFolders
                                    "printf failwithf"
            }
        let findProc = Process.SafeExecute (proc, Echo.All)
        if findProc.Output.StdOut.Trim().Length > 0 then
            Console.Error.WriteLine "Illegal usage of printf/printfn/sprintf/sprintfn/failwithf detected; use SPrintF1/SPrintF2/... instead"
            Environment.Exit 1

    FindOffendingPrintfUsage()

| Some(someOtherTarget) ->
    Console.Error.WriteLine("Unrecognized target: " + someOtherTarget)
    Environment.Exit 2
