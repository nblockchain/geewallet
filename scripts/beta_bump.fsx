#!/usr/bin/env fsharpi

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

let currentVersion = Misc.GetCurrentVersion(FsxHelper.RootDir)

let newVersion =
    // e.g. to bump from 0.7.x.y to 0.9.x.y
    Version(currentVersion.Major, currentVersion.Minor + 2, currentVersion.Build, currentVersion.Revision).ToString()

Process.Execute(
    {
        Command = "dotnet"
        Arguments = sprintf "fsi %s %s --auto" (Path.Combine(FsxHelper.ScriptsDir.FullName, "bump.fsx")) newVersion
    }, Echo.All
).UnwrapDefault() |> ignore<string>
