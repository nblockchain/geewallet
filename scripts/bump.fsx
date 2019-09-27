#!/usr/bin/env fsharpi

open System
open System.IO
#r "System.Configuration"
#load "InfraLib/Misc.fs"
#load "InfraLib/Process.fs"
#load "InfraLib/Git.fs"
open FSX.Infrastructure
open Process

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
        let args = Misc.FsxArguments()
        if args.Length > 0 then
            if args.Length > 1 then
                Console.Error.WriteLine "Only one argument supported, not more"
                Environment.Exit 1
                failwith "Unreachable"
            else
                let full = Version(args.Head)
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
    let proc1 =
        {
            Command = replaceScript
            Arguments = sprintf "%s %s"
                             (fullVersion.ToString())
                             (newFullVersion.ToString())
        }
    Process.SafeExecute (proc1, Echo.Off) |> ignore

    // to replace Android's versionCode attrib in AndroidManifest.xml
    let proc2 =
        {
            proc1 with
                Arguments = sprintf "versionCode=\\\"%s\\\" versionCode=\\\"%s\\\""
                             (androidVersion.ToString())
                             (newVersion.ToString())
        }
    Process.SafeExecute (proc2, Echo.Off) |> ignore

    fullVersion,newFullVersion


let GitCommit (fullVersion: Version) (newFullVersion: Version) =
    let gitAddJson =
        {
            Command = "git"
            Arguments = "add src/GWallet.Backend.Tests/*.json"
        }
    Process.SafeExecute (gitAddJson, Echo.Off) |> ignore
    let gitAddCommonAssemblyInfo =
        {
            Command = "git"
            Arguments = "add src/GWallet.Backend/Properties/CommonAssemblyInfo.fs"
        }
    Process.SafeExecute (gitAddCommonAssemblyInfo, Echo.Off) |> ignore
    let gitAddAndroidManifest =
        {
            Command = "git"
            Arguments = "add src/GWallet.Frontend.XF.Android/Properties/AndroidManifest.xml"
        }
    Process.SafeExecute (gitAddAndroidManifest, Echo.Off) |> ignore

    let commitMessage = sprintf "Bump version: %s -> %s" (fullVersion.ToString()) (newFullVersion.ToString())
    let finalCommitMessage =
        if IsStableRevision fullVersion.MinorRevision then
            sprintf "(Post)%s" commitMessage
        else
            commitMessage
    let gitCommit =
        {
            Command = "git"
            Arguments = sprintf "commit -m \"%s\"" finalCommitMessage
        }
    Process.SafeExecute (gitCommit,
                         Echo.Off) |> ignore

let GitTag (newFullVersion: Version) =
    if not (IsStableRevision newFullVersion.MinorRevision) then
        failwith "something is wrong, this script should tag only even(stable) minorRevisions, not odd(unstable) ones"

    let gitDeleteTag =
        {
            Command = "git"
            Arguments = sprintf "tag --delete %s" (newFullVersion.ToString())
        }
    Process.Execute (gitDeleteTag,
                     Echo.Off) |> ignore
    let gitCreateTag =
        {
            Command = "git"
            Arguments = sprintf "tag %s" (newFullVersion.ToString())
        }
    Process.SafeExecute (gitCreateTag,
                         Echo.Off) |> ignore

Console.WriteLine "Bumping..."
let fullUnstableVersion,newFullStableVersion = Bump true
GitCommit fullUnstableVersion newFullStableVersion
GitTag newFullStableVersion

Console.WriteLine (sprintf "Version bumped to %s, release binaries now and press any key when you finish."
                           (newFullStableVersion.ToString()))
Console.ReadKey true |> ignore

Console.WriteLine "Post-bumping..."
let fullStableVersion,newFullUnstableVersion = Bump false
GitCommit fullStableVersion newFullUnstableVersion

Console.WriteLine (sprintf "Version bumping finished. Remember to push via `git push <remote> <branch> %s`"
                           (newFullStableVersion.ToString()))
