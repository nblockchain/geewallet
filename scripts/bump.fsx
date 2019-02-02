#!/usr/bin/env fsharpi

open System
open System.IO
#load "Infra.fs"
open FSX.Infrastructure

let rootDir = DirectoryInfo(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let fullVersion = Misc.GetCurrentVersion(rootDir)
let androidVersion = fullVersion.MinorRevision

let newFullVersion,newVersion =
    if Util.FsxArguments().Length > 0 then
        if Util.FsxArguments().Length > 1 then
            Console.Error.WriteLine "Only one argument supported, not more"
            Environment.Exit 1
            failwith "Unreachable"
        else
            let full = Version(Util.FsxArguments().Head)
            full,full.MinorRevision
    else
        let newVersion = androidVersion + 1s
        let full = Version(sprintf "%i.%i.%i.%i"
                                   fullVersion.Major
                                   fullVersion.Minor
                                   fullVersion.Build
                                   newVersion)
        full,newVersion

let replaceScript = Path.Combine(__SOURCE_DIRECTORY__, "replace.fsx")
Process.SafeExecute (sprintf "%s %s %s"
                         replaceScript
                         (fullVersion.ToString())
                         (newFullVersion.ToString()),
                     Echo.Off) |> ignore
// to replace Android's versionCode attrib in AndroidManifest.xml
Process.SafeExecute (sprintf "%s versionCode=\\\"%s\\\" versionCode=\\\"%s\\\""
                         replaceScript
                         (androidVersion.ToString())
                         (newVersion.ToString()),
                     Echo.Off) |> ignore

Process.SafeExecute (sprintf "git add src/GWallet.Backend.Tests/*.json",
                     Echo.Off) |> ignore
Process.SafeExecute (sprintf "git add src/GWallet.Backend/Properties/CommonAssemblyInfo.fs",
                     Echo.Off) |> ignore
Process.SafeExecute (sprintf "git add src/GWallet.Frontend.XF.Android/Properties/AndroidManifest.xml",
                     Echo.Off) |> ignore
Process.SafeExecute (sprintf "git commit -m \"Bump version: %s -> %s\"" (fullVersion.ToString()) (newFullVersion.ToString()),
                     Echo.Off) |> ignore


Process.Execute (sprintf "git tag --delete %s" (newFullVersion.ToString()),
                 Echo.Off) |> ignore
Process.SafeExecute (sprintf "git tag %s" (newFullVersion.ToString()),
                     Echo.Off) |> ignore

Console.WriteLine (sprintf "Version bumped. Remember to push via `git push <remote> <branch> %s`"
                           (newFullVersion.ToString()))
