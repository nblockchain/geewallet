#!/usr/bin/env fsharpi

open System
open System.IO
#load "Infra.fs"
open FSX.Infrastructure

let IsStableRevision revision =
    (int revision % 2) = 0

let Bump(toStable: bool): Version*Version =
    let rootDir = DirectoryInfo(Path.Combine(__SOURCE_DIRECTORY__, ".."))
    let fullVersion = Misc.GetCurrentVersion(rootDir)
    let androidVersion = fullVersion.MinorRevision

    if toStable && IsStableRevision androidVersion then
        failwith "bump script expects you to be in unstable version currently, but we found a stable"
    if (not toStable) && (not (IsStableRevision androidVersion)) then
        failwith "sanity check failed, post-bump should happen in a stable version"

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

    fullVersion,newFullVersion


let GitCommit (fullVersion: Version) (newFullVersion: Version) =
    Process.SafeExecute (sprintf "git add src/GWallet.Backend.Tests/*.json",
                         Echo.Off) |> ignore
    Process.SafeExecute (sprintf "git add src/GWallet.Backend/Properties/CommonAssemblyInfo.fs",
                         Echo.Off) |> ignore
    Process.SafeExecute (sprintf "git add src/GWallet.Frontend.XF.Android/Properties/AndroidManifest.xml",
                         Echo.Off) |> ignore

    let commitMessage = sprintf "Bump version: %s -> %s" (fullVersion.ToString()) (newFullVersion.ToString())
    let finalCommitMessage =
        if IsStableRevision fullVersion.MinorRevision then
            sprintf "(Post)%s" commitMessage
        else
            commitMessage
    Process.SafeExecute (sprintf "git commit -m \"%s\"" finalCommitMessage,
                         Echo.Off) |> ignore

let GitTag (newFullVersion: Version) =
    if not (IsStableRevision newFullVersion.MinorRevision) then
        failwith "something is wrong, this script should tag only even(stable) minorRevisions, not odd(unstable) ones"

    Process.Execute (sprintf "git tag --delete %s" (newFullVersion.ToString()),
                     Echo.Off) |> ignore
    Process.SafeExecute (sprintf "git tag %s" (newFullVersion.ToString()),
                         Echo.Off) |> ignore

Console.WriteLine "Bumping..."
let fullUnstableVersion,newFullStableVersion = Bump true
GitCommit fullUnstableVersion newFullStableVersion
GitTag newFullStableVersion

Console.WriteLine (sprintf "Version bumped to %s, release binaries now and press key when you finish."
                           (newFullStableVersion.ToString()))
Console.Read() |> ignore

Console.WriteLine "Post-bumping..."
let fullStableVersion,newFullUnstableVersion = Bump false
GitCommit fullStableVersion newFullUnstableVersion

Console.WriteLine (sprintf "Version bumping finished. Remember to push via `git push <remote> <branch> %s`"
                           (newFullStableVersion.ToString()))
