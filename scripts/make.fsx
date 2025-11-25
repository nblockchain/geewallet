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
#load "fsx/Fsdk/Network.fs"
#load "fsx/Fsdk/Unix.fs"
#endif
open Fsdk
open Fsdk.Process

#load "fsxHelper.fs"
open GWallet.Scripting

let UNIX_NAME = "gwallet"
let DEFAULT_FRONTEND = "GWallet.Frontend.Console"
let BACKEND = "GWallet.Backend"

type BinaryConfig =
    | Debug
    | Release
    override self.ToString() =
        sprintf "%A" self

let rec private GatherTarget (args: string list, targetSet: Option<string>): Option<string> =
    match args with
    | [] -> targetSet
    | head::tail ->
        if (targetSet.IsSome) then
            failwith "only one target can be passed to make"
        GatherTarget (tail, Some (head))

#if LEGACY_FRAMEWORK
let PrintNugetVersion () =
    if not (FsxHelper.NugetExe.Exists) then
        false
    else
        let nugetProc =
            Network.RunNugetCommand
                FsxHelper.NugetExe
                String.Empty
                Echo.OutputOnly
                false
        match nugetProc.Result with
        | ProcessResultState.Success _ -> true
        | ProcessResultState.WarningsOrAmbiguous _output ->
            Console.WriteLine()
            Console.Out.Flush()

            failwith
                "nuget process succeeded but its output contained warnings ^"
        | ProcessResultState.Error(_exitCode, _output) ->
            Console.WriteLine()
            Console.Out.Flush()
            failwith "nuget process' output contained errors ^"
#endif

let BuildSolution
    (buildToolAndBuildArg: string*string)
    (solutionFileName: string)
    (binaryConfig: BinaryConfig)
    (maybeConstant: Option<string>)
    (extraOptions: string)
    =
    let buildTool,buildArg = buildToolAndBuildArg

    let configOption =
        if buildTool.StartsWith "dotnet" then
            sprintf "--configuration %s" (binaryConfig.ToString())
        else
            // TODO: use -property instead of /property when we don't need xbuild anymore
            sprintf "/property:Configuration=%s" (binaryConfig.ToString())

    let defineConstantsFromBuildConfig =
        match buildConfigContents |> Map.tryFind "DefineConstants" with
        | Some constants -> constants.Split([|";"|], StringSplitOptions.RemoveEmptyEntries) |> Seq.ofArray
        | None -> Seq.empty
    let defineConstantsSoFar =
        if not (buildTool.StartsWith "dotnet") then
            Seq.append ["LEGACY_FRAMEWORK"] defineConstantsFromBuildConfig
        else
            defineConstantsFromBuildConfig
    let allDefineConstants =
        match maybeConstant with
        | Some constant -> Seq.append [constant] defineConstantsSoFar
        | None -> defineConstantsSoFar
    let configOptions =
        if allDefineConstants.Any() then
            // FIXME: we shouldn't override the project's DefineConstants, but rather set "ExtraDefineConstants"
            // from the command line, and merge them later in the project file: see https://stackoverflow.com/a/32326853/544947
            let defineConstants =
                match binaryConfig with
                | Release -> allDefineConstants
                | Debug ->
                    if not (allDefineConstants.Contains "DEBUG") then
                        Seq.append allDefineConstants ["DEBUG"]
                    else
                        allDefineConstants

            let semiColon = ";"
            let semiColonEscaped = "%3B"
            match buildTool,Misc.GuessPlatform() with
            | "xbuild", _ ->
                // TODO: use -property instead of /property when we don't need xbuild anymore
                // xbuild: legacy of the legacy!
                // see https://github.com/dotnet/sdk/issues/9562
                sprintf "%s /property:DefineConstants=\"%s\"" configOption (String.Join(semiColonEscaped, defineConstants))
            | builtTool, Misc.Platform.Windows when buildTool.ToLower().Contains "msbuild" ->
                sprintf "%s -property:DefineConstants=\"%s\"" configOption (String.Join(semiColon, defineConstants))
            | _ ->
                sprintf "%s -property:DefineConstants=\\\"%s\\\"" configOption (String.Join(semiColon, defineConstants))
        else
            configOption
    let buildArgs = sprintf "%s %s %s %s"
                            buildArg
                            solutionFileName
                            configOptions
                            extraOptions
    let buildProcess = Process.Execute ({ Command = buildTool; Arguments = buildArgs }, Echo.All)
    match buildProcess.Result with
    | Error _ ->
        Console.WriteLine()
        Console.Error.WriteLine (sprintf "%s build failed" buildTool)
#if LEGACY_FRAMEWORK
        PrintNugetVersion() |> ignore
#endif
        Environment.Exit 1
    | _ -> ()

let JustBuild binaryConfig maybeConstant =
    let maybeBuildTool = Map.tryFind "BuildTool" buildConfigContents
    let mainSolution = "gwallet.sln"
    let buildTool,buildArg,solutionFileName =
        match maybeBuildTool with
        | None ->
            failwith "A BuildTool should have been chosen by the configure script, please report this bug"
        | Some "dotnet" ->
#if LEGACY_FRAMEWORK
            failwith "'dotnet' shouldn't be the build tool when using legacy framework, please report this bug"
#endif
            "dotnet", "build", mainSolution
        | Some otherBuildTool ->
#if LEGACY_FRAMEWORK
            let nugetConfig =
                Path.Combine(
                    FsxHelper.RootDir.FullName,
                    "NuGet.config")
                |> FileInfo
            let legacyNugetConfig =
                Path.Combine(
                    FsxHelper.RootDir.FullName,
                    "NuGet-legacy.config")
                |> FileInfo

            File.Copy(legacyNugetConfig.FullName, nugetConfig.FullName, true)
            otherBuildTool, String.Empty, "gwallet-legacy.sln"
#else
            otherBuildTool, String.Empty, mainSolution
#endif

    Console.WriteLine (sprintf "Building in %s mode..." (binaryConfig.ToString()))
    BuildSolution
        (buildTool, buildArg)
        solutionFileName
        binaryConfig
        maybeConstant
        String.Empty

let GetPathToFrontendBinariesDir (binaryConfig: BinaryConfig) =
#if LEGACY_FRAMEWORK
    Path.Combine (FsxHelper.RootDir.FullName, "src", DEFAULT_FRONTEND, "bin", binaryConfig.ToString())
#else
    Path.Combine (FsxHelper.RootDir.FullName, "src", DEFAULT_FRONTEND, "bin", binaryConfig.ToString(), "net6.0")
#endif

let GetPathToBackend () =
    Path.Combine (FsxHelper.RootDir.FullName, "src", BACKEND)

let MakeAll (maybeConstant: Option<string>) =
    let buildConfig = BinaryConfig.Debug
    JustBuild buildConfig maybeConstant
    buildConfig

let RunFrontend (buildConfig: BinaryConfig) (maybeArgs: Option<string>) =
    let frontEndExtension =
#if LEGACY_FRAMEWORK
        ".exe"
#else
        ".dll"
#endif

    let pathToFrontend =
        Path.Combine(GetPathToFrontendBinariesDir buildConfig, DEFAULT_FRONTEND + frontEndExtension) |> FileInfo

#if LEGACY_FRAMEWORK
    match Misc.GuessPlatform() with
    | Misc.Platform.Windows -> ()
    | _ -> Unix.ChangeMode(pathToFrontend, "+x", false)
#endif

    let fileName, finalArgs =
        match maybeArgs with
#if LEGACY_FRAMEWORK
        | None | Some "" -> pathToFrontend.FullName, String.Empty
        | Some args -> pathToFrontend.FullName, args
#else
        | None | Some "" -> "dotnet", pathToFrontend.FullName
        | Some args -> "dotnet", (sprintf "%s %s" pathToFrontend.FullName args)
#endif

    let startInfo = ProcessStartInfo(FileName = fileName, Arguments = finalArgs, UseShellExecute = false)
    startInfo.EnvironmentVariables.["MONO_ENV_OPTIONS"] <- "--debug"

    let proc = Process.Start startInfo
    proc.WaitForExit()
    proc

let maybeTarget = GatherTarget (Misc.FsxOnlyArguments(), None)
match maybeTarget with
| None ->
    MakeAll None |> ignore

| Some("release") ->
    JustBuild BinaryConfig.Release None

#if LEGACY_FRAMEWORK
| Some "nuget" ->
    Console.WriteLine "This target is for debugging purposes."

    if not (PrintNugetVersion()) then
        Console.Error.WriteLine "Nuget executable has not been downloaded yet, try `make` alone first"
        Environment.Exit 1
#endif

| Some("run") ->
    let buildConfig = MakeAll None
    RunFrontend buildConfig None
        |> ignore
