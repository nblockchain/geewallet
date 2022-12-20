#!/usr/bin/env fsharpi

open System
open System.IO

#r "System.Configuration"
open System.Configuration
#load "fsx/InfraLib/Misc.fs"
#load "fsx/InfraLib/Process.fs"
#load "fsx/InfraLib/Git.fs"
open FSX.Infrastructure
open Process

let ConfigCommandCheck (commandNamesByOrderOfPreference: seq<string>) (exitIfNotFound: bool): Option<string> =
    let rec configCommandCheck currentCommandNamesQueue allCommands =
        match Seq.tryHead currentCommandNamesQueue with
        | Some currentCommand ->
            Console.Write (sprintf "checking for %s... " currentCommand)
            if not (Process.CommandWorksInShell currentCommand) then
                Console.WriteLine "not found"
                configCommandCheck (Seq.tail currentCommandNamesQueue) allCommands
            else
                Console.WriteLine "found"
                currentCommand |> Some
        | None ->
            Console.Error.WriteLine (sprintf "configure: error, please install %s" (String.Join(" or ", List.ofSeq allCommands)))
            if exitIfNotFound then
                Environment.Exit 1
                failwith "unreachable"
            else
                None

    configCommandCheck commandNamesByOrderOfPreference commandNamesByOrderOfPreference


let rootDir = DirectoryInfo(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let initialConfigFile =
    match Misc.GuessPlatform() with
    | Misc.Platform.Windows ->
        // not using Mono anyway
        Map.empty

    | Misc.Platform.Mac ->
        ConfigCommandCheck ["mono"] true |> ignore

        // unlikely that anyone uses old Mono versions in Mac, as it's easy to update (TODO: detect anyway)
        Map.empty

    | Misc.Platform.Linux ->
        ConfigCommandCheck ["mono"] true |> ignore

        let pkgConfig = "pkg-config"
        ConfigCommandCheck [pkgConfig] true |> ignore

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
        Map.empty.Add("MonoPkgConfigVersion", monoVersion)

let targetsFileToExecuteNugetBeforeBuild = """<?xml version="1.0" encoding="utf-8"?>
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$([MSBuild]::GetDirectoryNameOfFileAbove($(MSBuildThisFileDirectory), NuGet.Restore.targets))\NuGet.Restore.targets"
          Condition=" '$(NuGetRestoreImported)' != 'true' " />
</Project>
"""
File.WriteAllText(Path.Combine(rootDir.FullName, "before.gwallet.sln.targets"),
                  targetsFileToExecuteNugetBeforeBuild)

let buildTool: string =
    match Misc.GuessPlatform() with
    | Misc.Platform.Linux | Misc.Platform.Mac ->
        ConfigCommandCheck ["make"] true |> ignore
        ConfigCommandCheck ["fsharpc"] true |> ignore

        // needed by NuGet.Restore.targets & the "update-servers" Makefile target
        ConfigCommandCheck ["curl"] true
            |> ignore

        if Misc.GuessPlatform() = Misc.Platform.Mac then
            match ConfigCommandCheck [ "msbuild"; "xbuild" ] true with
            | Some theBuildTool -> theBuildTool
            | _ -> failwith "unreachable"
        else
            // yes, msbuild tests for the existence of this file path below (a folder named xbuild, not msbuild),
            // because $MSBuildExtensionsPath32 evaluates to /usr/lib/mono/xbuild (for historical reasons)
            if File.Exists "/usr/lib/mono/xbuild/Microsoft/VisualStudio/v16.0/FSharp/Microsoft.FSharp.Targets" then
                match ConfigCommandCheck [ "msbuild"; "xbuild" ] true with
                | Some theBuildTool -> theBuildTool
                | _ -> failwith "unreachable"
            else
                // if the above file doesn't exist, even though F# is installed (because we already checked for 'fsharpc'),
                // the version installed is too old, and doesn't work with msbuild, so it's better to use xbuild
                match ConfigCommandCheck [ "xbuild" ] false with
                | None ->
                    Console.Error.WriteLine "An alternative to installing mono-xbuild is upgrading your F# installtion to v5.0"
                    Environment.Exit 1
                    failwith "unreachable"
                | Some xbuildCmd -> xbuildCmd

    | Misc.Platform.Windows ->
        //we need to call "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -find MSBuild\**\Bin\MSBuild.exe

        let programFiles = Environment.GetFolderPath Environment.SpecialFolder.ProgramFilesX86
        let vswhereExe = Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe") |> FileInfo
        ConfigCommandCheck (List.singleton vswhereExe.FullName) |> ignore

        let vswhereCmd =
            {
                Command = vswhereExe.FullName
                Arguments = "-find MSBuild\\**\\Bin\\MSBuild.exe"
            }
        let processResult = Process.Execute(vswhereCmd, Echo.Off)
        let msbuildPath = 
            processResult.UnwrapDefault().Split(
                Array.singleton Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries
            ).[0].Trim()
        msbuildPath

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

Console.WriteLine()
Console.WriteLine(sprintf
                      "\tConfiguration summary for gwallet %s %s"
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
                      buildTool)
Console.WriteLine()

Console.WriteLine "Configuration succeeded, you can now run `make`"
