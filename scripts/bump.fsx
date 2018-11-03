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
                                 (fullVersion.MajorRevision.ToString())
                                 (newVersion.ToString()))

let replaceScript = Path.Combine(__SOURCE_DIRECTORY__, "replace.fsx")
Process.Execute (sprintf "%s %s %s"
                         replaceScript
                         (fullVersion.ToString())
                         (newFullVersion.ToString()),
                 false, true)
Process.Execute (sprintf "%s \\\"%s\\\" \\\"%s\\\""
                         replaceScript
                         (androidVersion.ToString())
                         (newVersion.ToString()),
                 false, true)
