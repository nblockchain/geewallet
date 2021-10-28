namespace GWallet.Scripting

open System
open System.IO

open FSX.Infrastructure

module FsxHelper =

    let ScriptsDir = __SOURCE_DIRECTORY__ |> DirectoryInfo
    let RootDir = Path.Combine(ScriptsDir.FullName, "..") |> DirectoryInfo
    let NugetPackagesDir = Path.Combine(RootDir.FullName, "packages") |> DirectoryInfo

    let FsxRunner =
        match Misc.GuessPlatform() with
        | Misc.Platform.Windows ->
            Path.Combine(ScriptsDir.FullName, "fsi.bat")
        | _ ->
            let fsxRunnerEnvVar = Environment.GetEnvironmentVariable "FsxRunner"
            if String.IsNullOrEmpty fsxRunnerEnvVar then
                let msg = "FsxRunner env var not found, it should have been sourced from build.config file"
                let msgFull =
                    msg + Environment.NewLine +
                    "(maybe you meant to run a Makefile target rather than this script directly; or there is a .sh wrapper script for your .fsx script)"
                failwith msgFull
            fsxRunnerEnvVar
