#!/usr/bin/env fsharpi

open System
open System.IO
#load "Infra.fs"
open FSX.Infrastructure

let rootDir = DirectoryInfo(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let fullVersion = Misc.GetCurrentVersion(rootDir)
let androidVersion = fullVersion.MinorRevision

let newVersion = int androidVersion + 1
let newFullVersion = Version(sprintf "%s.%s.%s.%s"
                                 (fullVersion.Major.ToString())
                                 (fullVersion.Minor.ToString())
                                 (fullVersion.Build.ToString())
                                 (newVersion.ToString()))

let replaceScript = Path.Combine(__SOURCE_DIRECTORY__, "replace.fsx")
Process.SafeExecute (sprintf "%s %s %s"
                         replaceScript
                         (fullVersion.ToString())
                         (newFullVersion.ToString()),
                     Echo.Off) |> ignore
Process.SafeExecute (sprintf "%s \\\"%s\\\" \\\"%s\\\""
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
