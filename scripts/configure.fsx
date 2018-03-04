#!/usr/bin/env fsharpi

open System
open System.IO
open System.Collections.Generic
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
ConfigCommandCheck "xbuild"
ConfigCommandCheck "mono"

// needed by NuGet.Restore.targets
ConfigCommandCheck "curl"

let rec private GatherOrGetDefaultPrefix(args: string list, previousIsPrefixArg: bool, prefixSet: Option<string>): string =
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

File.WriteAllText(Path.Combine(__SOURCE_DIRECTORY__, "build.config"),
                  sprintf "Prefix=%s" prefix.FullName)

let rootDir = DirectoryInfo(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let version = Misc.GetCurrentVersion(rootDir)

let GetRepoInfo()=
    let rec GetBranchFromGitBranch(outchunks)=
        match outchunks with
        | [] -> failwith "current branch not found, unexpected output from `git branch`"
        | head::tail ->
            match head with
            | StdErr(errChunk) ->
                failwith ("unexpected stderr output from `git branch`: " + errChunk)
            | StdOut(outChunk) ->
                if (outChunk.StartsWith("*")) then
                    let branchName = outChunk.Substring("* ".Length)
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
                let branch = GetBranchFromGitBranch(gitBranch.Output)
                let gitLastCommit = Process.Execute("git log --no-color --first-parent -n1 --pretty=format:%h", false, true)
                if (gitLastCommit.ExitCode <> 0) then
                    failwith "Unexpected git behaviour, as `git log` succeeded before but not now"
                else if (gitLastCommit.Output.Length <> 1) then
                    failwith "Unexpected git output for special git log command"
                else
                    let lastCommitSingleOutput = gitLastCommit.Output.[0]
                    match lastCommitSingleOutput with
                    | StdErr(errChunk) ->
                        failwith ("unexpected stderr output from `git log` command: " + errChunk)
                    | StdOut(lastCommitHash) ->
                        sprintf "(%s/%s)" branch lastCommitHash

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
