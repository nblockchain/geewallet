#!/usr/bin/env fsharpi

open System
open System.IO
open System.Net
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

#r "System.Net.Http.dll"
#r "System.Web.Extensions.dll"
open System.Web.Script.Serialization
#load "githubActions.fs"
open GWallet.Github

type GitProvider =
    | GitHub
    | GitLab

let remotes = Git.CheckRemotes()
let orgOrUsername, repoName =
    match remotes.TryFind "origin" with
    | None -> failwith "Expecting 'origin' remote to be present"
    | Some remoteUrl ->
        if remoteUrl.StartsWith "git@" then
            failwithf "Expecting an https:// based repo URL but got %s" remoteUrl
        Console.WriteLine remoteUrl
        // example: https://gitlab-ci-token:[MASKED]@gitlab.com/nblockchain/geewallet.git
        let uri = Uri remoteUrl
        let repoName = uri.Segments.Last()
        let repoName =
            if repoName.EndsWith ".git" then
                repoName.Substring(0, repoName.Length - ".git".Length)
            else
                repoName
        Console.WriteLine repoName
        let orgOrUsername = (uri.Segments.SkipLast 1).Last().TrimEnd '/'
        Console.WriteLine orgOrUsername
        orgOrUsername, repoName

let webClient = new WebClient()
let mirror = sprintf "https://github.com/%s/%s" orgOrUsername repoName
try
    webClient.DownloadString mirror
    |> ignore
with
| _ ->
    Console.Error.WriteLine(
        sprintf "Some problem while retrieving '%s', did you set up a mirror properly from GitLab to GitHub?"
            mirror
    )

let snapFiles = FsxHelper.RootDir.EnumerateFiles().Where(fun file -> file.Name.EndsWith ".snap")
if not (snapFiles.Any()) then
    Console.Error.WriteLine "No snap package found."
    Environment.Exit 1

let snapFile = snapFiles.SingleOrDefault()
if null = snapFile then
    Console.Error.WriteLine "Too many snap packages found, please discard invalid/old ones first."
    Environment.Exit 2

let githubRef = Environment.GetEnvironmentVariable "GITHUB_REF"
let gitlabRef = Environment.GetEnvironmentVariable "CI_COMMIT_REF_SLUG"
let onlyCiMsg = "No github or gitlab variable found, beware: manual logging for release has been disabled, only automated CI jobs can upload now"
let gitProvider =
    if not (String.IsNullOrEmpty githubRef) then
        GitHub
    elif not (String.IsNullOrEmpty gitlabRef) then
        GitLab
    else
        failwith onlyCiMsg

let snapcraftLoginFileName = Path.Combine(FsxHelper.RootDir.FullName, "snapcraft.login")
if File.Exists snapcraftLoginFileName then
    Console.WriteLine "snapcraft.login file found, skipping log-in"
else
    match gitProvider with
    | GitHub ->
        let snapcraftLoginEnvVarValue = Environment.GetEnvironmentVariable "SNAPCRAFT_LOGIN"
        if String.IsNullOrEmpty snapcraftLoginEnvVarValue then
            failwith "SNAPCRAFT_LOGIN-prefixed env var not found; note: manual logging for release has been disabled, only automated CI jobs can upload now"
        File.WriteAllText(snapcraftLoginFileName, snapcraftLoginEnvVarValue)
    | _ ->
        if orgOrUsername = "nblockchain" && repoName = "geewallet" then
            failwith "In the case of GitLab, the 'snapcraft.login' file should already be here, copied by the docker scripts to the docker container"
        else
            // this must be a fork, do nothing
            ()

Console.WriteLine "Checking if this is a tag commit..."

let gitTag =
    match gitProvider with
    | GitHub ->
        let tagsPrefix = "refs/tags/"
        if not (githubRef.StartsWith tagsPrefix) then
            Console.WriteLine (sprintf "No tag being set (GITHUB_REF=%s), skipping release." githubRef)
            Environment.Exit 0
        githubRef.Substring tagsPrefix.Length

    | GitLab ->
        let commitHash = Git.GetLastCommit()
        let currentBranch = Environment.GetEnvironmentVariable "CI_COMMIT_REF_NAME"
        if String.IsNullOrEmpty currentBranch then
            failwith "CI_COMMIT_REF_NAME should be available when GitLab: https://docs.gitlab.com/ee/ci/variables/predefined_variables.html"

        GithubActions.MakeSureGithubCIPassed orgOrUsername repoName commitHash currentBranch

        let ciTag = Environment.GetEnvironmentVariable "CI_COMMIT_TAG"

        if String.IsNullOrEmpty ciTag then
            Console.WriteLine (sprintf "No tag being set (CI_COMMIT_TAG=%s), skipping release." ciTag)
            Environment.Exit 0
        ciTag

if not (snapFile.FullName.Contains gitTag) then
    failwithf "Git tag (%s) doesn't match version in snap package file name (%s)"
        gitTag
        snapFile.FullName

Console.WriteLine (sprintf "About to start upload of release %s" gitTag)

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
