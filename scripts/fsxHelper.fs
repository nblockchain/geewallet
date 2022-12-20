namespace GWallet.Scripting

open System
open System.IO

open FSX.Infrastructure

module FsxHelper =

    let ScriptsDir = __SOURCE_DIRECTORY__ |> DirectoryInfo
    let RootDir = Path.Combine(ScriptsDir.FullName, "..") |> DirectoryInfo
    let NugetDir = Path.Combine (RootDir.FullName, ".nuget") |> DirectoryInfo
    let NugetExe = Path.Combine (NugetDir.FullName, "nuget.exe") |> FileInfo
    let NugetSolutionPackagesDir = Path.Combine(RootDir.FullName, "packages") |> DirectoryInfo
    let NugetScriptsPackagesDir() =
        let dir = Path.Combine(NugetDir.FullName, "packages") |> DirectoryInfo
        if not dir.Exists then
            Directory.CreateDirectory dir.FullName
            |> ignore
        dir

    let FsxRunnerBin,FsxRunnerArg =
        match Misc.GuessPlatform() with
        | Misc.Platform.Windows ->
#if !LEGACY_FRAMEWORK
            "dotnet", "fsi"
#else
            Path.Combine(ScriptsDir.FullName, "fsx", "Tools", "fsi.bat"), String.Empty
#endif
        | _ ->
            let fsxRunnerBinEnvVar = Environment.GetEnvironmentVariable "FsxRunnerBin"
            let fsxRunnerArgEnvVar = Environment.GetEnvironmentVariable "FsxRunnerArg"
            if String.IsNullOrEmpty fsxRunnerBinEnvVar then
                let msg = "FsxRunnerBin env var not found, it should have been sourced from build.config file"
                let msgFull =
                    msg + Environment.NewLine +
                    "(maybe you meant to run a Makefile target rather than this script directly; or there is a .sh wrapper script for your .fsx script)"
                failwith msgFull
            fsxRunnerBinEnvVar, fsxRunnerArgEnvVar
