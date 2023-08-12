#!/usr/bin/env -S dotnet fsi

open System
open System.IO

#if !LEGACY_FRAMEWORK
#r "nuget: Fsdk, Version=0.6.0--date20230812-0646.git-2268d50"
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

let initialConfigFile, buildTool, areGtkLibsAbsentOrDoesNotApply =
    match Misc.GuessPlatform() with
    | Misc.Platform.Windows ->
        let buildTool=
            match Process.ConfigCommandCheck ["dotnet"] false true with
            | Some _ -> "dotnet"
            | None ->
                Console.Write "checking for msbuild... "
                match Process.VsWhere "MSBuild\\**\\Bin\\MSBuild.exe" with
                | None ->
                    Console.WriteLine "not found"
                    Console.Out.Flush()
                    Console.Error.WriteLine "Error, please install 'dotnet' aka .NET (6.0 or newer), and/or .NETFramework 4.x ('msbuild')"
                    Environment.Exit 1
                    failwith "Unreachable"
                | Some msbuildPath ->
                    Console.WriteLine "found"
                    msbuildPath

        Map.empty, buildTool, true
    | platform (* Unix *) ->

        Process.ConfigCommandCheck ["make"] true true |> ignore

        match Process.ConfigCommandCheck ["dotnet"] false true with
        | Some _ -> Map.empty, "dotnet", true
        | None ->

            Process.ConfigCommandCheck ["mono"] true true |> ignore
            Process.ConfigCommandCheck ["fsharpc"] true true |> ignore

            // needed by NuGet.Restore.targets & the "update-servers" Makefile target
            Process.ConfigCommandCheck ["curl"] true true
                |> ignore

            if platform = Misc.Platform.Mac then
                match Process.ConfigCommandCheck [ "msbuild"; "xbuild" ] true true with
                | Some theBuildTool -> Map.empty, theBuildTool, true
                | _ -> failwith "unreachable"
            else
                let buildTool =
                    // yes, msbuild tests for the existence of this file path below (a folder named xbuild, not msbuild),
                    // because $MSBuildExtensionsPath32 evaluates to /usr/lib/mono/xbuild (for historical reasons)
                    if File.Exists "/usr/lib/mono/xbuild/Microsoft/VisualStudio/v16.0/FSharp/Microsoft.FSharp.Targets" then
                        match Process.ConfigCommandCheck [ "msbuild"; "xbuild" ] true true with
                        | Some theBuildTool -> theBuildTool
                        | _ -> failwith "unreachable"
                    else
                        // if the above file doesn't exist, even though F# is installed (because we already checked for 'fsharpc'),
                        // the version installed is too old, and doesn't work with msbuild, so it's better to use xbuild
                        match Process.ConfigCommandCheck [ "xbuild" ] false true with
                        | None ->
                            Console.Error.WriteLine "An alternative to installing mono-xbuild is upgrading your F# installtion to v5.0"
                            Environment.Exit 1
                            failwith "unreachable"
                        | Some xbuildCmd -> xbuildCmd

                let pkgConfig = "pkg-config"
                Process.ConfigCommandCheck [pkgConfig] true true |> ignore

                let pkgName = "mono"
                let stableVersionOfMono = Version("6.6")
                Console.Write (sprintf "checking for %s v%s... " pkgName (stableVersionOfMono.ToString()))

                let pkgConfigCmd = { Command = pkgConfig
                                     Arguments = sprintf "--modversion %s" pkgName }
                let processResult = Process.Execute(pkgConfigCmd, Echo.Off)
                let monoVersion =
                    processResult
                        .Unwrap("Mono was found but not detected by pkg-config?")
                        .Trim()

                let currentMonoVersion = Version(monoVersion)
                if 1 = stableVersionOfMono.CompareTo currentMonoVersion then
                    Console.WriteLine "not found"
                    Console.Error.WriteLine (sprintf "configure: error, package requirements not met:")
                    Console.Error.WriteLine (sprintf "Please upgrade %s version from %s to (at least) %s"
                                                        pkgName
                                                        (currentMonoVersion.ToString())
                                                        (stableVersionOfMono.ToString()))
                    Environment.Exit 1
                Console.WriteLine "found"

                let areGtkLibsAbsentOrDoesNotApply =
                    if buildTool <> "msbuild" then
                        // because xbuild cannot build .NETStandard libs (and XF needs to be one)
                        true
                    else
                        Console.Write "checking for GTK (libs)... "
                        let gtkLibsPresent = FsxHelper.AreGtkLibsPresent Echo.Off
                        if gtkLibsPresent then
                            Console.WriteLine "found"
                        else
                            Console.WriteLine "not found"
                        not gtkLibsPresent

                Map.empty.Add("MonoPkgConfigVersion", monoVersion), buildTool, areGtkLibsAbsentOrDoesNotApply

#if LEGACY_FRAMEWORK
let targetsFileToExecuteNugetBeforeBuild = """<?xml version="1.0" encoding="utf-8"?>
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$([MSBuild]::GetDirectoryNameOfFileAbove($(MSBuildThisFileDirectory), NuGet.Restore.targets))\NuGet.Restore.targets"
          Condition=" '$(NuGetRestoreImported)' != 'true' " />
</Project>
"""
File.WriteAllText(Path.Combine(rootDir.FullName, "src", "before.gwallet.core-legacy.sln.targets"),
                  targetsFileToExecuteNugetBeforeBuild)
#endif

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

let lines =
    let toConfigFileLine (keyValuePair: System.Collections.Generic.KeyValuePair<string,string>) =
        sprintf "%s=%s" keyValuePair.Key keyValuePair.Value

    initialConfigFile.Add("Prefix", prefix.FullName)
                     .Add("BuildTool", buildTool)
    |> Seq.map toConfigFileLine
File.AppendAllLines(buildConfigFile.FullName, lines |> Array.ofSeq)

let version = Misc.GetCurrentVersion(rootDir)

let repoInfo = Git.GetRepoInfo()

let frontend =
    match buildTool, Misc.GuessPlatform() with

    // NOTE: 'dotnet build' cannot build Xamarin.Forms, and make.fsx doesn't support yet building
    // with both dotnet & msbuild yet
    | "dotnet", _ -> "Console"

    // because xbuild cannot build .NETStandard projects (and Frontend.XF proj needs to be)
    | _, Misc.Platform.Linux ->
        if areGtkLibsAbsentOrDoesNotApply then
            "Console"
        else
            "Xamarin.Forms"

    // NOTE: even though Windows has msbuild too, its buildTool value contains full path so it won't
    // match with the case below (and this is on purpose, since we don't build WinUI/UWP frontend yet)
    | "msbuild", _ ->
        "Xamarin.Forms"

    | _ -> "Console"

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
Console.WriteLine(sprintf
                      "\t* .NET build tool: %s"
                      (if buildTool = "dotnet" then "dotnet build" else buildTool))
Console.WriteLine(sprintf
                      "\t* Frontend: %s"
                      frontend)
Console.WriteLine()

Console.WriteLine "Configuration succeeded, you can now run `make`"
