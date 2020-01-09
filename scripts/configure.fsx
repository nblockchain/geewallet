#!/usr/bin/env fsharpi

open System
open System.IO

#r "System.Configuration"
#load "InfraLib/Misc.fs"
#load "InfraLib/Process.fs"
#load "InfraLib/Git.fs"
open FSX.Infrastructure
open Process

let ConfigCommandCheck (commandNamesByOrderOfPreference: seq<string>) =
    let rec configCommandCheck currentCommandNamesQueue allCommands =
        match Seq.tryHead currentCommandNamesQueue with
        | Some currentCommand ->
            Console.Write (sprintf "checking for %s... " currentCommand)
            if not (Process.CommandWorksInShell currentCommand) then
                Console.WriteLine "not found"
                configCommandCheck (Seq.tail currentCommandNamesQueue) allCommands
            else
                Console.WriteLine "found"
                currentCommand
        | None ->
            Console.Error.WriteLine (sprintf "configuration failed, please install %s" (String.Join(" or ", List.ofSeq allCommands)))
            Environment.Exit 1
            failwith "unreachable"
    configCommandCheck commandNamesByOrderOfPreference commandNamesByOrderOfPreference


let rootDir = DirectoryInfo(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let initialConfigFile,oldVersionOfMono =
    match Misc.GuessPlatform() with
    | Misc.Platform.Windows ->
        // not using Mono anyway
        Map.empty,false

    | Misc.Platform.Mac ->
        ConfigCommandCheck ["mono"] |> ignore

        // unlikely that anyone uses old Mono versions in Mac, as it's easy to update (TODO: detect anyway)
        Map.empty,false

    | Misc.Platform.Linux ->
        ConfigCommandCheck ["mono"] |> ignore

        let pkgConfig = "pkg-config"
        ConfigCommandCheck [pkgConfig] |> ignore
        let pkgConfigCmd = { Command = pkgConfig
                             Arguments = sprintf "--modversion mono" }
        let processResult = Process.Execute(pkgConfigCmd, Echo.Off)
        if processResult.ExitCode <> 0 then
            failwith "Mono was found but not detected by pkg-config?"

        let monoVersion = processResult.Output.StdOut.Trim()

        let versionOfMonoWhereArrayEmptyIsPresent = Version("5.8.1.0")
        let currentMonoVersion = Version(monoVersion)
        let oldVersionOfMono =
            1 = versionOfMonoWhereArrayEmptyIsPresent.CompareTo currentMonoVersion
        Map.empty.Add("MonoPkgConfigVersion", monoVersion),oldVersionOfMono

let targetsFileToExecuteNugetBeforeBuild = """<?xml version="1.0" encoding="utf-8"?>
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  {MaybeOverride}
  <Import Project="$([MSBuild]::GetDirectoryNameOfFileAbove($(MSBuildThisFileDirectory), NuGet.Restore.targets))\NuGet.Restore.targets"
          Condition=" '$(NuGetRestoreImported)' != 'true' " />
</Project>
"""

let nugetOverride = """<PropertyGroup>
    <!-- workaround for https://github.com/NuGet/Home/issues/6790 to override default
         Nuget URL specified in NuGet.Restore.targets file -->
    <NuGetUrl>https://dist.nuget.org/win-x86-commandline/v4.5.1/nuget.exe</NuGetUrl>
  </PropertyGroup>
"""
let targetsFileToGenerate =
    if oldVersionOfMono then
        targetsFileToExecuteNugetBeforeBuild.Replace("{MaybeOverride}", nugetOverride)
    else
        targetsFileToExecuteNugetBeforeBuild.Replace("{MaybeOverride}", String.Empty)

File.WriteAllText(Path.Combine(rootDir.FullName, "before.gwallet.sln.targets"),
                  targetsFileToGenerate)

let buildTool =
    match Misc.GuessPlatform() with
    | Misc.Platform.Linux | Misc.Platform.Mac ->
        ConfigCommandCheck ["make"] |> ignore
        ConfigCommandCheck ["fsharpc"] |> ignore

        // needed by NuGet.Restore.targets & the "update-servers" Makefile target
        ConfigCommandCheck ["curl"]
            |> ignore

        ConfigCommandCheck [ "msbuild"; "xbuild" ]
    | Misc.Platform.Windows ->
        let programFiles = Environment.GetFolderPath Environment.SpecialFolder.ProgramFilesX86
        let msbuildPathPrefix = Path.Combine(programFiles, "Microsoft Visual Studio", "2019")
        let GetMsBuildPath vsEdition =
            Path.Combine(msbuildPathPrefix, vsEdition, "MSBuild", "Current", "Bin", "MSBuild.exe")

        // FIXME: we should use vscheck.exe
        ConfigCommandCheck
            [
                GetMsBuildPath "Community"
                GetMsBuildPath "Enterprise"
                GetMsBuildPath "BuildTools"
            ]


let prefix = DirectoryInfo(Misc.GatherOrGetDefaultPrefix(Misc.FsxArguments(), false, None))

if not (prefix.Exists) then
    let warning = sprintf "WARNING: prefix doesn't exist: %s" prefix.FullName
    Console.Error.WriteLine warning

let lines =
    let toConfigFileLine (keyValuePair: System.Collections.Generic.KeyValuePair<string,string>) =
        sprintf "%s=%s" keyValuePair.Key keyValuePair.Value

    initialConfigFile.Add("Prefix", prefix.FullName)
                     .Add("BuildTool", buildTool)
    |> Seq.map toConfigFileLine

let path = Path.Combine(__SOURCE_DIRECTORY__, "build.config")
File.AppendAllLines(path, lines |> Array.ofSeq)

let version = Misc.GetCurrentVersion(rootDir)

let repoInfo = Git.GetRepoInfo()

Console.WriteLine()
Console.WriteLine(sprintf
                      "\tConfiguration summary for geewallet %s %s"
                      (version.ToString()) repoInfo)
Console.WriteLine()
Console.WriteLine(sprintf
                      "\t* Installation prefix: %s"
                      prefix.FullName)
Console.WriteLine()

Console.WriteLine "Configuration succeeded, you can now run `make`"
