#!/usr/bin/env -S dotnet fsi

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

#if !LEGACY_FRAMEWORK
#r "nuget: Fsdk, Version=0.6.0--date20230812-0646.git-2268d50"
#else
#r "System.Configuration"
open System.Configuration
#load "fsx/Fsdk/Misc.fs"
#load "fsx/Fsdk/Process.fs"
#load "fsx/Fsdk/Git.fs"
#endif
open Fsdk
open Fsdk.Process

#load "fsxHelper.fs"
open GWallet.Scripting

#if LEGACY_FRAMEWORK
#r "../.nuget/packages/Microsoft.Build.16.11.0/lib/net472/Microsoft.Build.dll"
#else
#r "nuget: Microsoft.Build, Version=16.11.0"
#endif
open Microsoft.Build.Construction


module MapHelper =
    let GetKeysOfMap (map: Map<'K,'V>): seq<'K> =
        map |> Map.toSeq |> Seq.map fst

    let MergeIntoMap<'K,'V when 'K: comparison> (from: seq<'K*'V>): Map<'K,seq<'V>> =
        let keys = from.Select (fun (k, v) -> k)
        let keyValuePairs =
            seq {
                for key in keys do
                    let valsForKey = (from.Where (fun (k, v) -> key = k)).Select (fun (k, v) -> v) |> seq
                    yield key,valsForKey
            }
        keyValuePairs |> Map.ofSeq

[<StructuralEquality; StructuralComparison>]
type private PackageInfo =
    {
        PackageId: string
        PackageVersion: string
        ReqReinstall: Option<bool>
    }

type private DependencyHolder =
    { Name: string }

[<CustomComparison; CustomEquality>]
type private ComparableFileInfo =
    {
        File: FileInfo
    }
    member self.DependencyHolderName: DependencyHolder =
        if self.File.FullName.ToLower().EndsWith ".nuspec" then
            { Name = self.File.Name }
        else
            { Name = self.File.Directory.Name + "/" }

    interface IComparable with
        member this.CompareTo obj =
            match obj with
            | null -> this.File.FullName.CompareTo null
            | :? ComparableFileInfo as other -> this.File.FullName.CompareTo other.File.FullName
            | _ -> invalidArg "obj" "not a ComparableFileInfo"
    override this.Equals obj =
        match obj with
        | :? ComparableFileInfo as other ->
            this.File.FullName.Equals other.File.FullName
        | _ -> false
    override this.GetHashCode () =
        this.File.FullName.GetHashCode ()

let FindOffendingPrintfUsage () =
    let findScript = Path.Combine (FsxHelper.RootDir.FullName, "scripts", "find.fsx")
    let excludeFolders =
        String.Format (
            "scripts{0}" +
            "src{1}GWallet.Frontend.Console{0}" +
            "src{1}GWallet.Backend.Tests{0}" +
            "src{1}GWallet.Backend{1}FSharpUtil.fs",
            Path.PathSeparator,
            Path.DirectorySeparatorChar
        )

    let fsxRunnerBin, fsxRunnerArg = FsxHelper.FsxRunnerInfo()
    let proc =
        {
            Command = fsxRunnerBin
            Arguments = sprintf "%s %s --exclude=%s %s"
                                fsxRunnerArg
                                findScript
                                excludeFolders
                                "printf failwithf"
        }
    let findProcOutput = Process.Execute(proc, Echo.All).UnwrapDefault()
    if findProcOutput.Trim().Length > 0 then
        Console.Error.WriteLine "Illegal usage of printf/printfn/sprintf/sprintfn/failwithf detected; use SPrintF1/SPrintF2/... instead"
        Environment.Exit 1


let SanityCheckNugetPackages () =
    let conventionDirectory =
        Path.Combine(FsxHelper.RootDir.FullName, "..", "conventions")

    let cloningResult =
        Process.Execute(
            { Command = "git"
              Arguments =
                sprintf
                    "clone -b SanityCheckStepSquashed https://github.com/Mersho/conventions.git %s"
                    conventionDirectory },
            Echo.Off
        )

    match cloningResult.Result with
    | Error _ ->
        Console.WriteLine()
        Console.Error.WriteLine "Clone failed."
        Environment.Exit 1

    | WarningsOrAmbiguous _ -> ()

    | _ -> ()

    let sanityCheckArgs =
        let sanityCheckExecuteCommand =
            sprintf
                "fsi %s"
                (Path.Combine(FsxHelper.RootDir.FullName, conventionDirectory, "scripts", "sanityCheckNuget.fsx"))

        sprintf
            "%s %s"
            sanityCheckExecuteCommand
            (FsxHelper.GetSolution SolutionFile.Default)
                .FullName


    Process
        .Execute(
            { Command = "dotnet"
              Arguments = sanityCheckArgs },
            Echo.All
        )
        .UnwrapDefault()
    |> ignore<string>


FindOffendingPrintfUsage()
#if !LEGACY_FRAMEWORK
SanityCheckNugetPackages()
#endif
