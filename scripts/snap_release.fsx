#!/usr/bin/env fsharpi

open System
open System.IO
open System.Linq
open System.Diagnostics

open System.Text
open System.Text.RegularExpressions
#r "System.Core.dll"
open System.Xml
#r "System.Xml.Linq.dll"
open System.Xml.Linq
open System.Xml.XPath

#r "System.Configuration"
open System.Configuration
#load "InfraLib/Misc.fs"
#load "InfraLib/Process.fs"
#load "InfraLib/Git.fs"
open FSX.Infrastructure
open Process

#load "fsxHelper.fs"
open GWallet.Scripting

let snapSubfolder =
    Path.Combine(FsxHelper.RootDir.FullName, "snap")
    |> DirectoryInfo

let snapFiles = snapSubfolder.EnumerateFiles().Where(fun file -> file.Name.EndsWith ".snap")
if not (snapFiles.Any()) then
    Console.Error.WriteLine "No snap package found."
    Environment.Exit 1

let snapFile = snapFiles.SingleOrDefault()
if null = snapFile then
    Console.Error.WriteLine "Too many snap packages found, please discard invalid/old ones first."
    Environment.Exit 2

let snapcraftLoginFileName = Path.Combine(FsxHelper.RootDir.FullName, "snapcraft.login")
if File.Exists snapcraftLoginFileName then
    Console.WriteLine "snapcraft.login file found, skipping log-in"
else
    let snapcraftLogin = Environment.GetEnvironmentVariable "SNAPCRAFT_LOGIN"
    if String.IsNullOrEmpty snapcraftLogin then
        failwith "Manual logging for release has been disabled, only automated CI jobs can upload now"
    else
        Console.WriteLine "Automatic login about to begin..."
        File.WriteAllText(snapcraftLoginFileName, snapcraftLogin)

// if this fails, use `snapcraft export-login` to generate a new token
Process.SafeExecute ({ Command = "snapcraft"; Arguments = "login --with snapcraft.login" }, Echo.All)
|> ignore

Console.WriteLine "Login successfull. Upload starting..."

// the 'stable' and 'candidate' channels require 'stable' grade in the yaml
Process.SafeExecute (
    {
        Command = "snapcraft"
        Arguments = sprintf "push %s --release=stable" snapFile.FullName
    }, Echo.All
) |> ignore
