#!/usr/bin/env fsharpi

open System
open System.IO

#r "System.Configuration"
#load "InfraLib/Misc.fs"
#load "InfraLib/Process.fs"
#load "InfraLib/Git.fs"
open FSX.Infrastructure
open Process

let rec ReplaceInDir (dir: DirectoryInfo) (oldString: string) (newString: string) =
    let ReplaceInFile (file: FileInfo) (oldString: string) (newString: string) =
        let oldText = File.ReadAllText file.FullName
        let newText = oldText.Replace(oldString, newString)
        if newText <> oldText then
            File.WriteAllText(file.FullName, newText)

    for file in dir.GetFiles() do
        if (file.Extension.ToLower() <> "dll") &&
           (file.Extension.ToLower() <> "exe") &&
           (file.Extension.ToLower() <> "png") then
            ReplaceInFile file oldString newString

    for subFolder in dir.GetDirectories() do
        if subFolder.Name <> ".git" then
            ReplaceInDir subFolder oldString newString

let args = Misc.FsxArguments()
let note = "NOTE: by default, some kind of files/folders will be excluded, e.g.: .git, *.dll, *.png, ..."
if args.Length > 2 then
    Console.Error.WriteLine "Can only pass two arguments: replace.fsx oldstring newstring"
    Console.WriteLine note
    Environment.Exit 1
elif args.Length < 2 then
    Console.Error.WriteLine "Need to pass two arguments: replace.fsx oldstring newstring"
    Console.WriteLine note
    Environment.Exit 1

let oldString = args.[0]
let newString = args.[1]

let startDir = DirectoryInfo (Directory.GetCurrentDirectory())

ReplaceInDir startDir oldString newString

