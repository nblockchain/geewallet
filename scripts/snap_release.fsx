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

if not (snapSubfolder.EnumerateFiles().Any(fun file -> file.Name.EndsWith ".snap")) then
    Console.Error.WriteLine "No snap package found."
    Environment.Exit 1

Console.WriteLine "Checking if this is a tag commit..."
let githubRef = Environment.GetEnvironmentVariable "GITHUB_REF"
if String.IsNullOrEmpty githubRef then
    failwith "GITHUB_REF var not found. Beware: manual logging for release has been disabled, only automated CI jobs can upload now"

let tagsPrefix = "refs/tags/"
if not (githubRef.StartsWith tagsPrefix) then
    Console.WriteLine (sprintf "No tag being set (GITHUB_REF=%s), skipping release." githubRef)
    Environment.Exit 0

let gitTag = githubRef.Substring tagsPrefix.Length
Console.WriteLine (sprintf "About to start upload of release %s" gitTag)

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
Console.WriteLine "Login successfull. Upload starting..."

// the 'stable' and 'candidate' channels require 'stable' grade in the yaml
Process.SafeExecute ({ Command = "snapcraft"; Arguments = "push snap/*.snap --release=stable" }, Echo.All)
