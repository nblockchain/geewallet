
namespace FSX.Infrastructure

open System
open System.IO
open System.Text
open System.Linq
open System.Threading
open System.Reflection
open System.Diagnostics

type OutChunk =
    | StdOut of StringBuilder
    | StdErr of StringBuilder
type OutputBuffer = list<OutChunk>
type Standard =
    | Output
    | Error
    override self.ToString() =
        sprintf "%A" self
type ProcessResult = { ExitCode: int; Output: OutputBuffer }

type Echo =
    | All
    | OutputOnly
    | Off

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

    // TODO: consider Android/iOS?
    type Platform =
    | Windows
    | Linux
    | Mac
    let GuessPlatform() =
        let macDirs = [ "/Applications"; "/System"; "/Users"; "/Volumes" ]
        match Environment.OSVersion.Platform with
        | PlatformID.MacOSX -> Platform.Mac
        | PlatformID.Unix ->
            if macDirs.All(fun dir -> Directory.Exists dir) then
                Platform.Mac
            else
                Platform.Linux
        | _ -> Platform.Windows


module Process =

    exception ProcessFailed of string

    let rec internal GetStdOut (outputBuffer: OutputBuffer) =
        match outputBuffer with
        | [] -> StringBuilder()
        | head::tail ->
            match head with
            | StdOut(out) ->
                GetStdOut(tail).Append(out.ToString())
            | _ ->
                GetStdOut(tail)

    let rec internal GetStdErr (outputBuffer: OutputBuffer) =
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

    type ProcessCouldNotStart(commandWithArguments, innerException: Exception) =
        inherit Exception(sprintf
            "Process could not start! %s" commandWithArguments, innerException)

    let Execute (commandWithArguments: string, echo: Echo)
        : ProcessResult =

        // I know, this shit below is mutable, but it's a consequence of dealing with .NET's Process class' events?
        let mutable outputBuffer: list<OutChunk> = []
        let outputBufferLock = new Object()


        let firstSpaceAt = commandWithArguments.IndexOf(" ")
        let (command, args) =
            if (firstSpaceAt >= 0) then
                (commandWithArguments.Substring(0, firstSpaceAt), commandWithArguments.Substring(firstSpaceAt + 1))
            else
                (commandWithArguments, String.Empty)

        if echo = Echo.All then
            Console.WriteLine commandWithArguments
            Console.Out.Flush()

        let startInfo = new ProcessStartInfo(command, args)
        startInfo.UseShellExecute <- false
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        use proc = new System.Diagnostics.Process()
        proc.StartInfo <- startInfo

        let ReadStandard(std: Standard) =

            let print =
                match std with
                | Standard.Output -> Console.Write : char array -> unit
                | Standard.Error -> Console.Error.Write

            let flush =
                match std with
                | Standard.Output -> Console.Out.Flush
                | Standard.Error -> Console.Error.Flush

            let outputToReadFrom =
                match std with
                | Standard.Output -> proc.StandardOutput
                | Standard.Error -> proc.StandardError

            let EndOfStream(readCount: int): bool =
                if (readCount > 0) then
                    false
                else if (readCount < 0) then
                    true
                else //if (readCount = 0)
                    outputToReadFrom.EndOfStream

            let ReadIteration(): bool =

                // I want to hardcode this to 1 because otherwise the order of the stderr|stdout
                // chunks in the outputbuffer would innecessarily depend on this bufferSize, setting
                // it to 1 makes it slow but then the order is only relying (in theory) on how the
                // streams come and how fast the .NET IO processes them
                let outChar = [|'x'|] // 'x' is a dummy value that will get replaced
                let bufferSize = 1
                let uniqueElementIndexInTheSingleCharBuffer = bufferSize - 1

                if not (outChar.Length = bufferSize) then
                    failwith "Buffer Size must equal current buffer size"

                let readTask = outputToReadFrom.ReadAsync(outChar, uniqueElementIndexInTheSingleCharBuffer, bufferSize)
                readTask.Wait()
                if not (readTask.IsCompleted) then
                    failwith "Failed to read"

                let readCount = readTask.Result
                if (readCount > bufferSize) then
                    failwith "StreamReader.Read() should not read more than the bufferSize if we passed the bufferSize as a parameter"

                if (readCount = bufferSize) then
                    if not (echo = Echo.Off) then
                        print outChar
                        flush()

                    lock outputBufferLock (fun _ ->

                        let leChar = outChar.[uniqueElementIndexInTheSingleCharBuffer]
                        match outputBuffer with
                        | [] ->
                            let newBuilder = StringBuilder(leChar.ToString())
                            let newBlock =
                                match std with
                                | Standard.Output -> StdOut newBuilder
                                | Standard.Error -> StdErr newBuilder
                            outputBuffer <- [ newBlock ]
                        | head::tail ->
                            match head with
                            | StdOut(out) ->
                                match std with
                                | Standard.Output ->
                                    out.Append outChar |> ignore
                                | Standard.Error ->
                                    let newErrBuilder = StdErr(StringBuilder(leChar.ToString()))
                                    outputBuffer <- newErrBuilder::outputBuffer
                            | StdErr(err) ->
                                match std with
                                | Standard.Error ->
                                    err.Append outChar |> ignore
                                | Standard.Output ->
                                    let newOutBuilder = StdOut(StringBuilder(leChar.ToString()))
                                    outputBuffer <- newOutBuilder::outputBuffer
                    )

                let continueIterating = not(EndOfStream(readCount))
                continueIterating

            // this is a way to do a `do...while` loop in F#...
            while (ReadIteration()) do
                ignore None

        let outReaderThread = new Thread(new ThreadStart(fun _ ->
            ReadStandard(Standard.Output)
        ))

        let errReaderThread = new Thread(new ThreadStart(fun _ ->
            ReadStandard(Standard.Error)
        ))

        try
            proc.Start() |> ignore
        with
        | e -> raise(ProcessCouldNotStart(commandWithArguments, e))

        outReaderThread.Start()
        errReaderThread.Start()
        proc.WaitForExit()
        let exitCode = proc.ExitCode

        outReaderThread.Join()
        errReaderThread.Join()

        { ExitCode = exitCode; Output = outputBuffer }

    let CommandCheck (commandName: string): Option<string> =
        let commandWhich = Execute (sprintf "which %s" commandName, Echo.Off)
        if (commandWhich.ExitCode <> 0) then
            None
        else
            Some(GetStdOut(commandWhich.Output).ToString())


    let rec private ExceptionIsOfTypeOrIncludesAnyInnerExceptionOfType(ex: Exception, t: Type): bool =
        if (ex = null) then
            false
        else if (ex.GetType() = t) then
            true
        else
            ExceptionIsOfTypeOrIncludesAnyInnerExceptionOfType(ex.InnerException, t)

    let rec private CheckIfCommandWorksInShellWithWhich (command: string): bool =
        let WhichCommandWorksInShell (): bool =
            let maybeResult =
                try
                    Some(Execute("which", Echo.Off))
                with
                | ex when (ExceptionIsOfTypeOrIncludesAnyInnerExceptionOfType(ex, typeof<System.ComponentModel.Win32Exception>))
                    -> None
                | _ -> reraise()

            match maybeResult with
            | None -> false
            | Some(_) -> true

        if not (WhichCommandWorksInShell()) then
            failwith "'which' doesn't work, please install it first"

        let whichProcResult = Execute("which " + command, Echo.Off)
        match whichProcResult.ExitCode with
        | 0 -> true
        | _ -> false

    let private HasWindowsExecutableExtension(path: string) =
        //FIXME: should do it in a case-insensitive way
        path.EndsWith(".exe") ||
            path.EndsWith(".bat") ||
            path.EndsWith(".cmd") ||
            path.EndsWith(".com")

    let private IsFileInWindowsPath(command: string) =
        let pathEnvVar = Environment.GetEnvironmentVariable("PATH")
        let paths = pathEnvVar.Split(Path.PathSeparator)
        paths.Any(fun path -> File.Exists(Path.Combine(path, command)))

    let CommandWorksInShell (command: string): bool =
        if (Misc.GuessPlatform() = Misc.Platform.Windows) then
            let exists = File.Exists(command) || IsFileInWindowsPath(command)
            if (exists && HasWindowsExecutableExtension(command)) then
                true
            else
                false
        else
            CheckIfCommandWorksInShellWithWhich(command)


    let SafeExecute (commandWithArguments: string, echo: Echo): ProcessResult =
        let procResult = Execute(commandWithArguments, echo)
        if not (procResult.ExitCode = 0) then
            if (echo = Echo.Off) then
                Console.WriteLine(GetStdOut(procResult.Output).ToString())
                Console.Error.WriteLine(GetStdErr(procResult.Output).ToString())
                Console.Error.WriteLine()
            raise(ProcessFailed(sprintf "Command '%s' failed with exit code %d"
                                        commandWithArguments procResult.ExitCode))
        procResult


module Unix =

    let mutable firstTimeSudoIsRun = true
    let private SudoInternal (command: string, safe: bool): Option<int> =
        if not (Process.CommandWorksInShell "id") then
            Console.WriteLine ("'id' unix command is needed for this script to work")
            Environment.Exit(2)

        let idProcResult = Process.Execute("id -u", Echo.Off)
        if (Process.GetStdOut(idProcResult.Output).ToString().Trim() = "0") then
            Console.Error.WriteLine ("Error: sudo privileges detected. Please don't run this directly with sudo or with the root user.")
            Environment.Exit(3)

        if (firstTimeSudoIsRun) then
            Process.SafeExecute("sudo -k", Echo.All) |> ignore

        Console.WriteLine("Attempting sudo for '{0}'", command)
        let result =
            if (safe) then
                Process.SafeExecute("sudo " + command, Echo.All) |> ignore
                None
            else
                let proc = Process.Execute("sudo " + command, Echo.All)
                proc.ExitCode |> Some
        firstTimeSudoIsRun <- false
        result

    let UnsafeSudo(command: string) =
        let res = SudoInternal(command, false)
        if (res.IsNone) then
            failwith "Abnormal None result from SudoInternal(_,false)"
        res.Value

    let Sudo(command: string): unit =
        SudoInternal(command, true) |> ignore

    type AptPackage =
    | Missing
    | ExistingVersion of string
    let IsAptPackageInstalled(packageName: string): AptPackage =
        if not (Process.CommandWorksInShell "dpkg") then
            Console.Error.WriteLine ("This script is only for debian-based distros, aborting.")
            Environment.Exit(3)

        let dpkgSearchProc = Process.Execute(sprintf "dpkg -s %s" packageName, Echo.Off)
        if not (dpkgSearchProc.ExitCode = 0) then
            AptPackage.Missing
        else
            let dpkgLines = Process.GetStdOut(dpkgSearchProc.Output).ToString().Split([|Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries)
            let versionTag = "Version: "
            let maybeVersion = dpkgLines.Where(fun line -> line.StartsWith(versionTag)).Single()
            let version = maybeVersion.Substring(versionTag.Length)
            AptPackage.ExistingVersion(version)

    let InstallAptPackageIfNotAlreadyInstalled(packageName: string)=
        if (IsAptPackageInstalled(packageName) = AptPackage.Missing) then
            Console.WriteLine("Installing {0}...", packageName)
            Sudo(String.Format("apt -y install {0}", packageName)) |> ignore

    let rec DownloadAptPackage (packageName: string) =
        Console.WriteLine()
        Console.WriteLine(sprintf "Downloading %s..."  packageName)
        let procResult = Process.Execute("apt download " + packageName, Echo.OutputOnly)
        if (procResult.ExitCode = 0) then
            Console.WriteLine("Downloaded " + packageName)
            ()
        else if (Process.GetStdErr(procResult.Output).ToString().Contains("E: Can't select candidate version from package")) then
            Console.WriteLine()
            Console.WriteLine()
            Console.WriteLine("Virtual package '{0}' found, provided by:", packageName)
            InstallAptPackageIfNotAlreadyInstalled("aptitude")
            let aptitudeShowProc = Process.SafeExecute("aptitude show " + packageName, Echo.Off)
            let lines = Process.GetStdOut(aptitudeShowProc.Output).ToString().Split([|Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries)
            for line in lines do
                if (line.StartsWith("Provided by:")) then
                    Console.WriteLine(line.Substring("Provided by:".Length))
                    Console.Write("Choose the package from the list above: ")
                    let pkg = Console.ReadLine()
                    DownloadAptPackage(pkg)
        else
            failwith "The 'apt download' command ended with error"

    let DownloadAptPackageRecursively (packageName: string) =
        InstallAptPackageIfNotAlreadyInstalled("apt-rdepends")
        let aptRdependsProc = Process.SafeExecute("apt-rdepends " + packageName, Echo.Off)
        let lines = Process.GetStdOut(aptRdependsProc.Output).ToString().Split([|Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries)
        for line in lines do
            if not (line.Trim().Contains("Depends:")) then
                DownloadAptPackage(line.Trim())

    let DownloadAptPackagesRecursively (packages: string seq) =
        for pkg in packages do
            DownloadAptPackageRecursively(pkg)


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

