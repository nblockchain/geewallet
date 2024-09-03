#!/usr/bin/env fsharpi

open System
open System.IO

#if !LEGACY_FRAMEWORK
failwith "This script is not prepared yet for dotnet6 or higher, it assumes old mono is installed. If you intended to use this for mono, then run it with fsharpi --define:LEGACY_FRAMEWORK instead of dotnet fsi."
#else
#r "System.Configuration"
open System.Configuration
#load "fsx/Fsdk/Misc.fs"
#load "fsx/Fsdk/Process.fs"
#load "fsx/Fsdk/Unix.fs"
open Fsdk
open Fsdk.Process

if Misc.GuessPlatform() <> Misc.Platform.Linux then
    failwith "This script is only supported on Linux"

let monoAptSource = FileInfo "/etc/apt/sources.list.d/mono-official-stable.list"
if monoAptSource.Exists then
    Unix.PurgeAllPackagesFromAptSource monoAptSource
    Unix.Sudo(sprintf "rm %s" monoAptSource.FullName)
    Unix.AptUpdate()
#endif

