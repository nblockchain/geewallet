
namespace FSX.Infrastructure

open System
open System.IO
open System.Text
open System.Linq
open System.Threading
open System.Reflection
open System.Diagnostics

type OutChunk = StdOut of string | StdErr of string
type OutputBuffer = list<OutChunk>
type ProcessResult = { ExitCode: int; Output: OutputBuffer }

module Misc =

    let IsPathEqual (a, b): bool =
        // FIXME: do case-insensitive comparison in Windows and Mac
        a = b

    let private CopyToOverwrite(from: FileInfo, toPath: string, overwrite: bool) =
        try
            if (overwrite) then
                from.CopyTo(toPath, true) |> ignore
            else
                from.CopyTo(toPath) |> ignore
        with
        | _ ->
            Console.Error.WriteLine("Error while trying to copy {0} to {1}", from.FullName, toPath)
            reraise()


    let rec CopyDirectoryRecursively(sourceDir: DirectoryInfo,
                                     targetDir: DirectoryInfo) =
        // FIXME: make this an arg
        let excludeBasePaths = []

        sourceDir.Refresh()
        if not (sourceDir.Exists) then
            raise (new ArgumentException("Source directory does not exist: " + sourceDir.FullName, "sourceDir"))

        Directory.CreateDirectory(targetDir.FullName) |> ignore

        for sourceFile in sourceDir.GetFiles() do
            if (excludeBasePaths.Any(fun x -> IsPathEqual(Path.Combine(sourceDir.FullName, x), sourceFile.FullName))) then
                ()
            else
                CopyToOverwrite(sourceFile, Path.Combine(targetDir.FullName, sourceFile.Name), true)

        for sourceSubFolder in sourceDir.GetDirectories() do
            if (excludeBasePaths.Any(fun x -> IsPathEqual(Path.Combine(sourceDir.FullName, x), sourceSubFolder.FullName))) then
                ()
            else
                CopyDirectoryRecursively(sourceSubFolder, DirectoryInfo(Path.Combine(targetDir.FullName, sourceSubFolder.Name)))

    let GetCurrentVersion(dir: DirectoryInfo): Version =
        let assemblyVersionFileName = "CommonAssemblyInfo.fs"
        let assemblyVersionFsFile =
            (Directory.EnumerateFiles (dir.FullName,
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
            Console.Error.WriteLine "Format unexpected in version string (expecting a starting double quote), cannot extract version number"
            Environment.Exit 1

        let versionNumberEndPosInLine = lineContainingVersionNumber.IndexOf("\"", versionNumberStartPosInLine + 1)
        if (versionNumberEndPosInLine = -1) then
            Console.Error.WriteLine "Format unexpected in version string (expecting an ending double quote), cannot extract version number"
            Environment.Exit 1

        let version = lineContainingVersionNumber.Substring(versionNumberStartPosInLine + 1,
                                                    versionNumberEndPosInLine - versionNumberStartPosInLine - 1)
        Version(version)


module Process =

    let rec private GetStdOut (outputBuffer: OutputBuffer) =
        match outputBuffer with
        | [] -> StringBuilder()
        | head::tail ->
            match head with
            | StdOut(out) ->
                GetStdOut(tail).Append(out.ToString())
            | _ ->
                GetStdOut(tail)

    let rec private GetStdErr (outputBuffer: OutputBuffer) =
        match outputBuffer with
        | [] -> StringBuilder()
        | head::tail ->
            match head with
            | StdErr(err) ->
                GetStdErr(tail).Append(err.ToString())
            | _ ->
                GetStdErr(tail)

    let rec PrintToScreen (outputBuffer: OutputBuffer) =
        match outputBuffer with
        | [] -> ()
        | head::tail ->
            match head with
            | StdOut(out) ->
                Console.WriteLine(out)
                Console.Out.Flush()
            | StdErr(err) ->
                Console.Error.WriteLine(err)
                Console.Error.Flush()
            PrintToScreen(tail)

    let Execute (commandWithArguments: string, echo: bool, hidden: bool)
        : ProcessResult =

        // I know, this shit below is mutable, but it's a consequence of dealing with .NET's Process class' events
        let outputBuffer = new System.Collections.Generic.List<OutChunk>()
        let outputBufferLock = new Object()

        use outWaitHandle = new AutoResetEvent(false)
        use errWaitHandle = new AutoResetEvent(false)

        if (echo) then
            Console.WriteLine(commandWithArguments)

        let firstSpaceAt = commandWithArguments.IndexOf(" ")
        let (command, args) =
            if (firstSpaceAt >= 0) then
                (commandWithArguments.Substring(0, firstSpaceAt), commandWithArguments.Substring(firstSpaceAt + 1))
            else
                (commandWithArguments, String.Empty)

        let startInfo = new ProcessStartInfo(command, args)
        startInfo.UseShellExecute <- false
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        use proc = new System.Diagnostics.Process()
        proc.StartInfo <- startInfo

        let outReceived (e: DataReceivedEventArgs): unit =
            if (e.Data = null) then
                outWaitHandle.Set() |> ignore
            else
                if not (hidden) then
                    Console.WriteLine(e.Data)
                    Console.Out.Flush()
                lock outputBufferLock (fun _ -> outputBuffer.Add(OutChunk.StdOut(e.Data)))

        let errReceived (e: DataReceivedEventArgs): unit =
            if (e.Data = null) then
                errWaitHandle.Set() |> ignore
            else
                if not (hidden) then
                    Console.Error.WriteLine(e.Data)
                    Console.Error.Flush()
                lock outputBufferLock (fun _ -> outputBuffer.Add(OutChunk.StdErr(e.Data)))

        proc.OutputDataReceived.Add outReceived
        proc.ErrorDataReceived.Add errReceived

        proc.Start() |> ignore

        let exitCode =
            try
                proc.BeginOutputReadLine()
                proc.BeginErrorReadLine()

                proc.WaitForExit()
                proc.ExitCode

            finally
                outWaitHandle.WaitOne() |> ignore
                errWaitHandle.WaitOne() |> ignore

        { ExitCode = exitCode; Output = List.ofSeq(outputBuffer) }

    let CommandCheck (commandName: string): Option<string> =
        let commandWhich = Execute (sprintf "which %s" commandName, false, true)
        if (commandWhich.ExitCode <> 0) then
            None
        else
            Some(GetStdOut(commandWhich.Output).ToString())

module Util =

    let private FileMatchesIfArgumentIsAPath(argument: string, file: FileInfo) =
        try
            FileInfo(argument).FullName.Equals(file.FullName)
        with
        | _ -> false

    let private ExtensionMatchesIfArgumentIsAPath(argument: string, extension: string) =
        try
            Path.GetFileName(argument).EndsWith("." + extension)
        with
        | _ -> false

    let private currentExe =
        FileInfo(Uri(Assembly.GetEntryAssembly().CodeBase).LocalPath)

    let rec private FsxArgumentsInternalFsx(args: string list) =
        match args with
        | [] -> []
        | head::tail ->
            if FileMatchesIfArgumentIsAPath(head, currentExe) then
                tail
            else
                FsxArgumentsInternalFsx(tail)

    let rec private FsxArgumentsInternalFsi(args: string list, fsxFileFound: bool) =
        match args with
        | [] -> []
        | head::tail ->
            match fsxFileFound with
            | false ->
                if ExtensionMatchesIfArgumentIsAPath(head, "fsx") then
                    FsxArgumentsInternalFsi(tail, true)
                else
                    FsxArgumentsInternalFsi(tail, false)
            | true ->
                if (head.Equals("--")) then
                    tail
                else
                    args

    let FsxArguments() =
        let cmdLineArgs = Environment.GetCommandLineArgs() |> List.ofSeq
        let isFsi = (currentExe.Name = "fsi.exe")
        if (isFsi) then
            FsxArgumentsInternalFsi(cmdLineArgs, false)

        // below for #!/usr/bin/fsx shebang
        else
            FsxArgumentsInternalFsx(cmdLineArgs)

