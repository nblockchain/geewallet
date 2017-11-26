#!/usr/bin/env fsharpi

open System
open System.IO

#load "Build.fs"

#r "System.Configuration"
#load "fsx/InfraLib/MiscTools.fs"
#load "fsx/InfraLib/ProcessTools.fs"
open FSX.Infrastructure
open ProcessTools

let DEFAULT_FRONTEND = "GWallet.Frontend.Console"

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

let GatherPrefix(): string =
    let buildConfig = FileInfo (Path.Combine (__SOURCE_DIRECTORY__, "build.config"))
    if not (buildConfig.Exists) then
        Console.Error.WriteLine "ERROR: configure hasn't been run yet, run ./configure.sh first"
        Environment.Exit 1
    let buildConfigContents = File.ReadAllText buildConfig.FullName
    (buildConfigContents.Substring ("Prefix=".Length)).Trim()

let prefix = GatherPrefix ()
let libInstallPath = DirectoryInfo (Path.Combine (prefix, "lib", "gwallet"))
let binInstallPath = DirectoryInfo (Path.Combine (prefix, "bin"))

let launcherScriptPath = FileInfo (Path.Combine (__SOURCE_DIRECTORY__, "bin", "gwallet"))
let mainBinariesPath = DirectoryInfo (Path.Combine(__SOURCE_DIRECTORY__,
                                                   "src", DEFAULT_FRONTEND, "bin", "Debug"))

let wrapperScript = """#!/bin/sh
set -e
exec mono "$TARGET_DIR/$GWALLET_PROJECT.exe" "$@"
"""

let JustBuild binaryConfig =
    Console.WriteLine "Compiling gwallet..."
    let configOption = sprintf "/p:Configuration=%s" (binaryConfig.ToString())
    ProcessTools.SafeExecute ({ Command = "xbuild"; Arguments = configOption }, Echo.All) |> ignore

    Directory.CreateDirectory(launcherScriptPath.Directory.FullName) |> ignore
    let wrapperScriptWithPaths =
        wrapperScript.Replace("$TARGET_DIR", libInstallPath.FullName)
                     .Replace("$GWALLET_PROJECT", DEFAULT_FRONTEND)
    File.WriteAllText (launcherScriptPath.FullName, wrapperScriptWithPaths)

let MakeCheckCommand (commandName: string) =
    if not (ProcessTools.CommandWorksInShell commandName) then
        Console.Error.WriteLine (sprintf "%s not found, please install it first" commandName)
        Environment.Exit 1

let GetPathToFrontend (binaryConfig: BinaryConfig) =
    Path.Combine ("src", DEFAULT_FRONTEND, "bin", binaryConfig.ToString())

let maybeTarget = GatherTarget (MiscTools.FsxArguments(), None)
match maybeTarget with
| None ->
    JustBuild BinaryConfig.Debug

| Some("release") ->
    JustBuild BinaryConfig.Release

| Some("zip") ->
    let zipCommand = "zip"
    MakeCheckCommand zipCommand

    let version = GWallet.Build.GetCurrentVersion().ToString()

    let release = BinaryConfig.Release
    JustBuild release
    let binDir = "bin"
    Directory.CreateDirectory(binDir) |> ignore

    let zipName = sprintf "gwallet.v.%s.zip" version
    let pathToZip = Path.Combine(binDir, zipName)
    if (File.Exists (pathToZip)) then
        File.Delete (pathToZip)

    let pathToFrontend = GetPathToFrontend release
    let zipParams = sprintf "-j -r %s %s"
                            pathToZip pathToFrontend
    let zipRun = ProcessTools.Execute({ Command = zipCommand; Arguments = zipParams }, Echo.All)
    if (zipRun.ExitCode <> 0) then
        Console.Error.WriteLine "ZIP compression failed"
        Environment.Exit 1

| Some("check") ->
    Console.WriteLine "Running tests..."
    Console.WriteLine ()

    let nunitCommand = "nunit-console"
    MakeCheckCommand nunitCommand
    let nunitRun = ProcessTools.Execute({ Command = nunitCommand;
                                          Arguments = "src/GWallet.Backend.Tests/bin/GWallet.Backend.Tests.dll" },
                                        Echo.All)
    if (nunitRun.ExitCode <> 0) then
        Console.Error.WriteLine "Tests failed"
        Environment.Exit 1

| Some("install") ->
    Console.WriteLine "Installing gwallet..."
    Console.WriteLine ()
    MiscTools.CopyDirectoryRecursively (mainBinariesPath, libInstallPath, [])

    let finalPrefixPathOfWrapperScript = FileInfo (Path.Combine(binInstallPath.FullName, launcherScriptPath.Name))
    if not (Directory.Exists(finalPrefixPathOfWrapperScript.Directory.FullName)) then
        Directory.CreateDirectory(finalPrefixPathOfWrapperScript.Directory.FullName) |> ignore
    File.Copy(launcherScriptPath.FullName, finalPrefixPathOfWrapperScript.FullName, true)
    ProcessTools.SafeExecute({ Command = "chmod";
                               Arguments = sprintf "ugo+x %s" finalPrefixPathOfWrapperScript.FullName },
                             Echo.OutputOnly) |> ignore

| Some("run") ->
    if not (ProcessTools.CommandWorksInShell "mono") then
        Console.Error.WriteLine "mono not found? install it first"
        Environment.Exit 1

    let debug = BinaryConfig.Debug
    JustBuild debug

    let pathToFrontend = Path.Combine(GetPathToFrontend debug, DEFAULT_FRONTEND + ".exe")

    ProcessTools.SafeExecute({ Command = "mono"; Arguments = pathToFrontend }, Echo.All) |> ignore

| Some(someOtherTarget) ->
    Console.Error.WriteLine("Unrecognized target: " + someOtherTarget)
    Environment.Exit 2
