#!/usr/bin/env fsharpi

open System
open System.IO
open System.Linq

#load "Infra.fs"
open FSX.Infrastructure

let ConfigCommandCheck (commandName: string) =
    Console.Write (sprintf "checking for %s... " commandName)
    if (Process.CommandCheck commandName).IsNone then
        Console.Error.WriteLine "not found"
        Console.Error.WriteLine (sprintf "configuration failed, please install \"%s\"" commandName)
        Environment.Exit 1
    Console.WriteLine "found"

ConfigCommandCheck "fsharpc"
ConfigCommandCheck "mono"

// needed by NuGet.Restore.targets & the "update-servers" Makefile target
ConfigCommandCheck "curl"

let oldVersionOfMono =
    // we need this check because Ubuntu 18.04 LTS still brings a very old version of Mono (4.6.2) with no msbuild
    let versionOfMonoWhereTheRuntimeBugWasFixed = "5.4"

    match Misc.GuessPlatform() with
    | Misc.Platform.Windows ->
        // not using Mono anyway
        false
    | Misc.Platform.Mac ->
        // unlikely that anyone uses old Mono versions in Mac, as it's easy to update (TODO: detect anyway)
        false
    | Misc.Platform.Linux ->
        let pkgConfig = "pkg-config"
        ConfigCommandCheck pkgConfig
        let pkgConfigCmd = sprintf "%s --atleast-version=%s mono" pkgConfig versionOfMonoWhereTheRuntimeBugWasFixed
        let processResult = Process.Execute(pkgConfigCmd, false, false)
        processResult.ExitCode <> 0

let buildTool =
    if oldVersionOfMono then
        "xbuild"
    else
        "msbuild"
ConfigCommandCheck buildTool

let rec private GatherOrGetDefaultPrefix(args: List<string>, previousIsPrefixArg: bool, prefixSet: Option<string>): string =
    let GatherPrefix(newPrefix: string): Option<string> =
        match prefixSet with
        | None -> Some(newPrefix)
        | _ -> failwith ("prefix argument duplicated")

    let prefixArgWithEquals = "--prefix="
    match args with
    | [] ->
        match prefixSet with
        | None -> "/usr/local"
        | Some(prefix) -> prefix
    | head::tail ->
        if (previousIsPrefixArg) then
            GatherOrGetDefaultPrefix(tail, false, GatherPrefix(head))
        else if head = "--prefix" then
            GatherOrGetDefaultPrefix(tail, true, prefixSet)
        else if head.StartsWith(prefixArgWithEquals) then
            GatherOrGetDefaultPrefix(tail, false, GatherPrefix(head.Substring(prefixArgWithEquals.Length)))
        else
            failwith (sprintf "argument not recognized: %s" head)

let prefix = DirectoryInfo(GatherOrGetDefaultPrefix(Util.FsxArguments(), false, None))

if not (prefix.Exists) then
    let warning = sprintf "WARNING: prefix doesn't exist: %s" prefix.FullName
    Console.Error.WriteLine warning

let lines =
    let toConfigFileLine (keyValuePair: System.Collections.Generic.KeyValuePair<string,string>) =
        sprintf "%s=%s" keyValuePair.Key keyValuePair.Value

    Map.empty.Add("Prefix", prefix.FullName)
             .Add("BuildTool", buildTool)
    |> Seq.map toConfigFileLine

let path = Path.Combine(__SOURCE_DIRECTORY__, "build.config")
File.WriteAllLines(path, lines |> Array.ofSeq)

let rootDir = DirectoryInfo(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let version = Misc.GetCurrentVersion(rootDir)

let GetRepoInfo()=
    let rec GetBranchFromGitBranch(outchunks: list<string>)=
        match outchunks with
        | [] -> failwith "current branch not found, unexpected output from `git branch`"
        | head::tail ->
            if (head.StartsWith("*")) then
                let branchName = head.Substring("* ".Length)
                branchName
            else
                GetBranchFromGitBranch(tail)

    let gitWhich = Process.Execute("which git", false, true)
    if (gitWhich.ExitCode <> 0) then
        String.Empty
    else
        let gitLog = Process.Execute("git log --oneline", false, true)
        if (gitLog.ExitCode <> 0) then
            String.Empty
        else
            let gitBranch = Process.Execute("git branch", false, true)
            if (gitBranch.ExitCode <> 0) then
                failwith "Unexpected git behaviour, as `git log` succeeded but `git branch` didn't"
            else
                let branchesOutput = Process.GetStdOut(gitBranch.Output).ToString().Split([|Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries) |> List.ofSeq
                let branch = GetBranchFromGitBranch(branchesOutput)
                let gitLastCommit = Process.Execute("git log --no-color --first-parent -n1 --pretty=format:%h", false, true)
                if (gitLastCommit.ExitCode <> 0) then
                    failwith "Unexpected git behaviour, as `git log` succeeded before but not now"
                else if (gitLastCommit.Output.Length <> 1) then
                    failwith "Unexpected git output for special git log command"
                else
                    let lastCommitSingleOutput = gitLastCommit.Output.[0]
                    match lastCommitSingleOutput with
                    | StdErr(errChunk) ->
                        failwith ("unexpected stderr output from `git log` command: " + errChunk.ToString())
                    | StdOut(lastCommitHash) ->
                        sprintf "(%s/%s)" branch (lastCommitHash.ToString())

let repoInfo = GetRepoInfo()

Console.WriteLine()
Console.WriteLine(sprintf
                      "\tConfiguration summary for gwallet %s %s"
                      (version.ToString()) repoInfo)
Console.WriteLine()
Console.WriteLine(sprintf
                      "\t* Installation prefix: %s"
                      prefix.FullName)
Console.WriteLine()
