#!/usr/bin/env fsharpi

open System
open System.IO
open System.Linq

#r "System.Configuration"
#load "InfraLib/Misc.fs"
#load "InfraLib/Process.fs"
#load "InfraLib/Git.fs"
open FSX.Infrastructure
open Process

let FindInFile (file: FileInfo)
               (maybeExcludeItems: Option<seq<FileSystemInfo>>)
               (someStrings: seq<string>)
                   : unit =
    let doIt () =
        for line in File.ReadLines file.FullName do
            for someString in someStrings do
                if line.IndexOf someString >= 0 then
                    printfn "%s: %s" file.FullName line

    match maybeExcludeItems with
    | None ->
        doIt ()
    | Some excludeItems ->
        if excludeItems.All(fun entryToExclude -> entryToExclude.FullName <> file.FullName) then
            doIt ()

let rec FindExcludingDir (dir: DirectoryInfo)
                         (maybeExcludeItems: Option<seq<FileSystemInfo>>)
                         (someStrings: seq<string>)
                             : unit =
    let doIt () =
        for file in dir.GetFiles() do
            if file.Extension.ToLower() <> ".dll" &&
               file.Extension.ToLower() <> ".exe" &&
               file.Extension.ToLower() <> ".png" then
                FindInFile file maybeExcludeItems someStrings
        for subFolder in dir.GetDirectories() do
            if subFolder.Name <> ".git" &&
               subFolder.Name <> "obj" &&
               subFolder.Name <> "bin" &&
               subFolder.Name <> "packages" then
                FindExcludingDir subFolder maybeExcludeItems someStrings
    match maybeExcludeItems with
    | None ->
        doIt ()
    | Some excludeItems ->
        if excludeItems.All(fun entryToExclude -> entryToExclude.FullName <> dir.FullName) then
            doIt ()

let args = Misc.FsxArguments()

let note = "NOTE: by default, some kind of files/folders will be excluded, e.g.: .git/, packages/, bin/, obj/, *.exe, *.dll, *.png, ..."

if args.Length < 1 then
    Console.Error.WriteLine "Please pass at least 1 argument, with optional flag: find.fsx [-x=someDirToExclude,someFileToExclude] someString"
    Console.WriteLine note
    Environment.Exit 1

let firstArg = args.[0]

let excludeParticularFileSystemEntries =
    if firstArg.StartsWith "--exclude=" || firstArg.StartsWith "-x=" then
        firstArg.Substring(firstArg.IndexOf("=")+1) |> Some
    else
        None

let startDir = Directory.GetCurrentDirectory() |> DirectoryInfo
match excludeParticularFileSystemEntries with
| None ->
    let someStrings = args
    FindExcludingDir startDir None someStrings
| Some excludeList ->
    let someStrings = args.Skip(1)
    let entriesToExclude =
        excludeList.Split([|Path.PathSeparator|], StringSplitOptions.RemoveEmptyEntries)
    let excludeItems =
        seq {
            for entry in entriesToExclude do
                let dir = entry |> DirectoryInfo
                let file = entry |> FileInfo
                if dir.Exists then
                    yield dir :> FileSystemInfo
                elif file.Exists then
                    yield file :> FileSystemInfo
                else
                    failwithf "Directory or file '%s' doesn't exist" dir.FullName
        }
    FindExcludingDir startDir (Some excludeItems) someStrings

