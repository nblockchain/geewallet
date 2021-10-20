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
                failwith "FsxRunner env var not found, it should have been passed to make.sh (are you running this script on its own instead of invoking the Makefile target?)"
            fsxRunnerEnvVar
