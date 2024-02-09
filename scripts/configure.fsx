#!/usr/bin/env -S dotnet fsi

open System
open System.IO

#if !LEGACY_FRAMEWORK
#r "nuget: Fsdk, Version=0.6.0--date20231031-0834.git-2737eea"
#else
#r "System.Configuration"
open System.Configuration
#load "fsx/Fsdk/Misc.fs"
#load "fsx/Fsdk/Process.fs"
#load "fsx/Fsdk/Git.fs"
#endif
open Fsdk
open Fsdk.Process

#load "fsxHelper.fs"
open GWallet.Scripting

let rootDir = DirectoryInfo(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let stableVersionOfMono = Version("6.6")

let buildTool, legacyBuildTool, areGtkLibsAbsentOrDoesNotApply =

    let dotnetCmd = Process.ConfigCommandCheck ["dotnet"] false true

    match Misc.GuessPlatform() with
    | Misc.Platform.Windows ->
        let msbuildCmd =
            Console.Write "checking for msbuild... "
            match Process.VsWhere "MSBuild\\**\\Bin\\MSBuild.exe" with
            | None ->
                Console.WriteLine "not found"
                None
            | Some msbuildPath ->
                Console.WriteLine "found"
                Some msbuildPath

        dotnetCmd, msbuildCmd, true
    | platform (* Unix *) ->

        // because it comes from configure.sh's "Checking for a working F# REPL..."
        Console.WriteLine " found"

        Process.ConfigCommandCheck ["make"] true true |> ignore

        match Process.ConfigCommandCheck ["mono"] false true with
        | None ->
            dotnetCmd, None, true
        | Some _ ->

            match Process.ConfigCommandCheck ["fsharpc"] false true with
            | None ->
                dotnetCmd, None, true
            | Some _ ->

                if platform = Misc.Platform.Mac then
                    let msBuildOrXBuild = Process.ConfigCommandCheck [ "msbuild"; "xbuild" ] false true
                    dotnetCmd, msBuildOrXBuild, true
                else

                    let pkgConfig = "pkg-config"

                    match Process.ConfigCommandCheck [ pkgConfig ] false true with
                    | None -> dotnetCmd, None, true
                    | Some _ ->

                        // yes, msbuild tests for the existence of this file path below (a folder named xbuild, not msbuild),
                        // because $MSBuildExtensionsPath32 evaluates to /usr/lib/mono/xbuild (for historical reasons)
                        let fsharpTargetsFileExists =
                            File.Exists
                                "/usr/lib/mono/xbuild/Microsoft/VisualStudio/v16.0/FSharp/Microsoft.FSharp.Targets"

                        if not fsharpTargetsFileExists then
                            Console.Error.WriteLine
                                "WARNING: old F# version found, only xbuild can work with it (not msbuild, even if installed)"

                            Console.Error.WriteLine
                                "NOTE: an alternative to installing 'mono-xbuild' pkg is upgrading your F# installtion to v5.0"

                        let maybeXbuild = Process.ConfigCommandCheck [ "xbuild" ] false true

                        let maybeMsbuild =
                            let msbuildCheck = Process.ConfigCommandCheck [ "msbuild" ] false true

                            if fsharpTargetsFileExists then
                                msbuildCheck
                            else
                                None

                        let pkgName = "mono"
                        Console.Write(sprintf "checking for %s v%s... " pkgName (stableVersionOfMono.ToString()))

                        let pkgConfigCmd =
                            { Command = pkgConfig
                              Arguments = sprintf "--modversion %s" pkgName }

                        let processResult = Process.Execute(pkgConfigCmd, Echo.Off)

                        let monoVersion =
                            processResult
                                .Unwrap("Mono was found but not detected by pkg-config?")
                                .Trim()

                        let currentMonoVersion = Version(monoVersion)

                        // NOTE: see what 1 means here: https://learn.microsoft.com/en-us/dotnet/api/system.version.compareto?view=netframework-4.7
                        if 1 = stableVersionOfMono.CompareTo currentMonoVersion then
                            Console.WriteLine "not found"
                            dotnetCmd, None, true
                        else
                            Console.WriteLine "found"

                            let areGtkLibsAbsentOrDoesNotApply =
                                match dotnetCmd, maybeMsbuild, maybeXbuild with
                                | None, None, None ->
                                    // well, configure.fsx will not finish in this case anyway
                                    true
                                | Some _ , None, None ->
                                    // xbuild or msbuild is needed to compile XF.Gtk project
                                    true
                                | None, None, _ ->
                                    // xbuild alone cannot build .NETStandard2.0 libs (Backend and XF are)
                                    true
                                | _, _, _ ->
                                    Console.Write "checking for GTK (libs)..."
                                    let gtkLibsPresent = FsxHelper.AreGtkLibsPresent Echo.Off

                                    if gtkLibsPresent then
                                        Console.WriteLine "found"
                                    else
                                        Console.WriteLine "not found"

                                    not gtkLibsPresent

                            let legacyBuildTool =
                                if maybeMsbuild.IsSome then
                                    maybeMsbuild
                                else
                                    maybeXbuild

                            dotnetCmd, legacyBuildTool, areGtkLibsAbsentOrDoesNotApply

if buildTool.IsNone && legacyBuildTool.IsNone then
    Console.Out.Flush()
    Console.Error.WriteLine "configure: error, package requirements not met:"

    match Misc.GuessPlatform() with
    | Misc.Platform.Windows ->
        Console.Error.WriteLine "Please install 'dotnet' aka .NET (6.0 or newer), and/or .NETFramework 4.x ('msbuild')"
    | _ ->
        Console.Error.WriteLine (
            sprintf
                "Please install dotnet v6 (or newer), and/or Mono (msbuild or xbuild needed) v%s (or newer)"
                (stableVersionOfMono.ToString())
        )

    Environment.Exit 1

let prefix = DirectoryInfo(Misc.GatherOrGetDefaultPrefix(Misc.FsxOnlyArguments(), false, None))

if not (prefix.Exists) then
    let warning = sprintf "WARNING: prefix doesn't exist: %s" prefix.FullName
    Console.Error.WriteLine warning

let buildConfigFile =
    Path.Combine(__SOURCE_DIRECTORY__, "build.config")
    |> FileInfo

let fsxRunner =
    let fsxRunnerBinText = "FsxRunnerBin="
    let fsxRunnerArgText = "FsxRunnerArg="
    let buildConfigContents = File.ReadAllLines buildConfigFile.FullName
    match Array.tryFind (fun (line: string) -> line.StartsWith fsxRunnerBinText) buildConfigContents with
    | Some fsxRunnerBinLine ->
        let runnerBin = fsxRunnerBinLine.Substring fsxRunnerBinText.Length
        match Array.tryFind (fun (line: string) -> line.StartsWith fsxRunnerArgText) buildConfigContents with
        | Some fsxRunnerArgLine ->
            let runnerArg = fsxRunnerArgLine.Substring fsxRunnerArgText.Length
            if String.IsNullOrEmpty runnerArg then
                runnerBin
            else
                sprintf "%s %s" runnerBin runnerArg
        | _ ->
            runnerBin
    | _ ->
        failwithf
            "Element '%s' not found in %s file, configure.sh|configure.bat should have injected it, please report this bug"
            fsxRunnerBinText
            buildConfigFile.Name

let AddToDefinedConstants (constant: string) (configMap: Map<string, string>) =
    let configKey = "DefineConstants"
    
    match configMap.TryFind configKey with
    | None ->
        configMap
        |> Map.add configKey constant
    | Some previousConstants ->
        configMap
        |> Map.add configKey (sprintf "%s;%s" previousConstants constant)
    

let configFileToBeWritten =
    let initialConfigFile = Map.empty.Add("Prefix", prefix.FullName)

    let configFileStageTwo =
        match legacyBuildTool with
        | Some theTool -> initialConfigFile.Add("LegacyBuildTool", theTool)
        | None -> initialConfigFile

    let configFileStageThree =
        match buildTool with
        | Some theTool -> configFileStageTwo.Add("BuildTool", theTool)
        | None -> configFileStageTwo

    let finalConfigFile =
        let nativeSegwitEnabled =
            Misc.FsxOnlyArguments()
            |> List.contains "--native-segwit"
        if nativeSegwitEnabled then
            configFileStageThree
            |> AddToDefinedConstants "NATIVE_SEGWIT"
        else
            configFileStageThree

    finalConfigFile

let lines =
    let toConfigFileLine (keyValuePair: System.Collections.Generic.KeyValuePair<string,string>) =
        sprintf "%s=%s" keyValuePair.Key keyValuePair.Value

    configFileToBeWritten |> Seq.map toConfigFileLine

File.AppendAllLines(buildConfigFile.FullName, lines |> Array.ofSeq)

let version = Misc.GetCurrentVersion(rootDir)

let repoInfo = Git.GetRepoInfo()

let frontend =
    if areGtkLibsAbsentOrDoesNotApply then
        "Console"
    else
        "Xamarin.Forms"


Console.WriteLine()
Console.WriteLine(sprintf
                      "\tConfiguration summary for geewallet %s %s"
                      (version.ToString()) repoInfo)
Console.WriteLine()
Console.WriteLine(sprintf
                      "\t* Installation prefix: %s"
                      prefix.FullName)
Console.WriteLine(sprintf
                      "\t* F# script runner: %s"
                      fsxRunner)

match buildTool with
| Some _ -> Console.WriteLine "\t* Build tool: dotnet build"
| None -> ()

match legacyBuildTool with
| Some cmd -> Console.WriteLine(sprintf "\t* Legacy build tool: %s" cmd)
| None -> ()

Console.WriteLine(sprintf
                      "\t* Frontend: %s"
                      frontend)
Console.WriteLine()

Console.WriteLine "Configuration succeeded, you can now run `make`"
