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

let snapFiles = FsxHelper.RootDir.EnumerateFiles().Where(fun file -> file.Name.EndsWith ".snap")
if not (snapFiles.Any()) then
    Console.Error.WriteLine "No snap package found."
    Environment.Exit 1

let snapFile = snapFiles.SingleOrDefault()
if null = snapFile then
    Console.Error.WriteLine "Too many snap packages found, please discard invalid/old ones first."
    Environment.Exit 2

Console.WriteLine "Checking if this is a tag commit..."
let githubRef = Environment.GetEnvironmentVariable "GITHUB_REF"
if String.IsNullOrEmpty githubRef then
    failwith "GITHUB_REF var not found. Beware: manual logging for release has been disabled, only automated CI jobs can upload now"

let tagsPrefix = "refs/tags/"
if not (githubRef.StartsWith tagsPrefix) then
    Console.WriteLine (sprintf "No tag being set (GITHUB_REF=%s), skipping release." githubRef)
    Environment.Exit 0

let gitTag = githubRef.Substring tagsPrefix.Length
if not (snapFile.FullName.Contains gitTag) then
    Console.Error.WriteLine (
        sprintf "Git tag (%s) doesn't match version in snap package file name (%s)"
            gitTag
            snapFile.FullName
    )
    Environment.Exit 3

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
|> ignore

Console.WriteLine "Login successfull. Upload starting..."

// the 'stable' and 'candidate' channels require 'stable' grade in the yaml
Process.SafeExecute (
    {
        Command = "snapcraft"
        Arguments = sprintf "push %s --release=beta" snapFile.FullName
    }, Echo.All
) |> ignore
