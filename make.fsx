#!/usr/bin/env fsharpi

open System
open System.IO
#load "Infra.fs"
open FSX.Infrastructure

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
    Console.WriteLine "Gathering gwallet dependencies..."
    let nuget = Process.Execute ("nuget restore", true, false)
    if (nuget.ExitCode <> 0) then
        Environment.Exit 1

    Console.WriteLine "Compiling gwallet..."
    let configOption = sprintf "/p:Configuration=%s" (binaryConfig.ToString())
    let xbuild = Process.Execute (sprintf "xbuild %s" configOption, true, false)
    if (xbuild.ExitCode <> 0) then
        Environment.Exit 1

    Directory.CreateDirectory(launcherScriptPath.Directory.FullName) |> ignore
    let wrapperScriptWithPaths =
        wrapperScript.Replace("$TARGET_DIR", libInstallPath.FullName)
                     .Replace("$GWALLET_PROJECT", DEFAULT_FRONTEND)
    File.WriteAllText (launcherScriptPath.FullName, wrapperScriptWithPaths)

let MakeCheckCommand (commandName: string) =
    if (Process.CommandCheck commandName).IsNone then
        Console.Error.WriteLine (sprintf "%s not found, please install it first" commandName)
        Environment.Exit 1

let maybeTarget = GatherTarget (Util.FsxArguments(), None)
match maybeTarget with
| None ->
    JustBuild BinaryConfig.Debug

| Some("release") ->
    JustBuild BinaryConfig.Release

| Some("zip") ->
    let zipCommand = "zip"
    MakeCheckCommand zipCommand

    let version = Misc.GetCurrentVersion().ToString()

    JustBuild BinaryConfig.Release
    let binDir = "bin"
    Directory.CreateDirectory(binDir) |> ignore

    let zipName = sprintf "gwallet.v.%s.zip" version

    let zipLaunch = sprintf "%s -r %s/%s src/%s/bin/%s"
                            zipCommand binDir zipName DEFAULT_FRONTEND (BinaryConfig.Release.ToString())
    let zipRun = Process.Execute(zipLaunch, true, false)
    if (zipRun.ExitCode <> 0) then
        Console.Error.WriteLine "Tests failed"
        Environment.Exit 1

| Some("check") ->
    Console.WriteLine "Running tests..."
    Console.WriteLine ()

    let nunitCommand = "nunit-console"
    MakeCheckCommand nunitCommand
    let nunitRun = Process.Execute(sprintf "%s src/GWallet.Backend.Tests/bin/GWallet.Backend.Tests.dll" nunitCommand,
                                   true, false)
    if (nunitRun.ExitCode <> 0) then
        Console.Error.WriteLine "Tests failed"
        Environment.Exit 1

| Some("install") ->
    Console.WriteLine "Installing gwallet..."
    Console.WriteLine ()
    Directory.CreateDirectory(libInstallPath.FullName) |> ignore
    Misc.CopyDirectoryRecursively (mainBinariesPath, libInstallPath)

    let finalPrefixPathOfWrapperScript = FileInfo (Path.Combine(binInstallPath.FullName, launcherScriptPath.Name))
    if not (Directory.Exists(finalPrefixPathOfWrapperScript.Directory.FullName)) then
        Directory.CreateDirectory(finalPrefixPathOfWrapperScript.Directory.FullName) |> ignore
    File.Copy(launcherScriptPath.FullName, finalPrefixPathOfWrapperScript.FullName, true)
    if ((Process.Execute(sprintf "chmod ugo+x %s" finalPrefixPathOfWrapperScript.FullName, false, true)).ExitCode <> 0) then
        failwith "Unexpected chmod failure, please report this bug"

| Some("run") ->
    let fullPathToMono = Process.CommandCheck "mono"
    if (fullPathToMono.IsNone) then
        Console.Error.WriteLine "mono not found? install it first"
        Environment.Exit 1

    let debug = BinaryConfig.Debug
    JustBuild debug
    let proc = System.Diagnostics.Process.Start
                   (fullPathToMono.Value,
                    sprintf "src/%s/bin/%s/%s.exe" DEFAULT_FRONTEND (debug.ToString()) DEFAULT_FRONTEND)
    proc.WaitForExit()

| Some(someOtherTarget) ->
    Console.Error.WriteLine("Unrecognized target: " + someOtherTarget)
    Environment.Exit 2
