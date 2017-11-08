namespace GWallet

open System
open System.IO
open System.Linq

module Build =

    let GetCurrentVersion(): Version =
        let assemblyVersionFileName = "CommonAssemblyInfo.fs"
        let assemblyVersionFsFile =
            (Directory.EnumerateFiles (__SOURCE_DIRECTORY__,
                                       assemblyVersionFileName,
                                       SearchOption.AllDirectories)).SingleOrDefault ()
        if (assemblyVersionFsFile = null) then
            Console.Error.WriteLine (sprintf "%s not found in any subfolder (or found too many), cannot extract version number"
                                         assemblyVersionFileName)
            Environment.Exit 1

        let assemblyVersionAttribute = "AssemblyVersion"
        let lineContainingVersionNumber =
            File.ReadLines(assemblyVersionFsFile).SingleOrDefault (fun line -> (not (line.Trim().StartsWith ("//"))) && line.Contains (assemblyVersionAttribute))

        if (lineContainingVersionNumber = null) then
            Console.Error.WriteLine (sprintf "%s attribute not found in %s (or found too many), cannot extract version number"
                                         assemblyVersionAttribute assemblyVersionFsFile)
            Environment.Exit 1

        let versionNumberStartPosInLine = lineContainingVersionNumber.IndexOf("\"")
        if (versionNumberStartPosInLine = -1) then
            Console.Error.WriteLine "Format unexpected in version string (expecting a stating double quote), cannot extract version number"
            Environment.Exit 1

        let versionNumberEndPosInLine = lineContainingVersionNumber.IndexOf("\"", versionNumberStartPosInLine + 1)
        if (versionNumberEndPosInLine = -1) then
            Console.Error.WriteLine "Format unexpected in version string (expecting an ending double quote), cannot extract version number"
            Environment.Exit 1

        let version = lineContainingVersionNumber.Substring(versionNumberStartPosInLine + 1,
                                                            versionNumberEndPosInLine - versionNumberStartPosInLine - 1)
        Version(version)

