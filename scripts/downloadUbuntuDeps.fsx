#!/usr/bin/env fsharpi

open System
open System.IO

#load "Infra.fs"
open FSX.Infrastructure

let outputFolder = "bin"
let deps = "fsharp"::[]

let currentDir = Directory.GetCurrentDirectory()
let binDir = Path.Combine(currentDir, outputFolder)
if not (Directory.Exists binDir) then
    Directory.CreateDirectory binDir |> ignore
Directory.SetCurrentDirectory binDir

try
    Unix.DownloadAptPackagesRecursively deps
    Console.WriteLine (sprintf "Success. All your files are in the '%s' subfolder." outputFolder)
finally
    Directory.SetCurrentDirectory currentDir
