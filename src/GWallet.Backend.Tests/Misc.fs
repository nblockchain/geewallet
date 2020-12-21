namespace FSX.Infrastructure

open System
open System.IO
open System.Linq

// comes from https://gitlab.com/nblockchain/fsx/-/blob/master/InfraLib/Misc.fs
module Misc =

    type Platform =
    | Windows
    | Linux
    | Mac

    let GuessPlatform() =
        let macDirs = [ "/Applications"; "/System"; "/Users"; "/Volumes" ]
        match Environment.OSVersion.Platform with
        | PlatformID.MacOSX ->
            Platform.Mac
        | PlatformID.Unix ->
            if macDirs.All(fun dir -> Directory.Exists dir) then
                Platform.Mac
            else
                Platform.Linux
        | _ ->
            Platform.Windows
