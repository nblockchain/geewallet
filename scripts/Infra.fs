
namespace FSX.Infrastructure

open System
open System.IO
open System.Text
open System.Linq
open System.Threading
open System.Reflection
open System.Diagnostics

type Standard =
    | Output
    | Error
    override self.ToString() =
        sprintf "%A" self

type OutputChunk =
    { OutputType: Standard; Chunk: StringBuilder }

type OutputBuffer(buffer: list<OutputChunk>) =

    //NOTE both Filter() and Print() process tail before head
    // because of the way the buffer-aggregation is implemented in
    // Execute()'s ReadIteration()

    let rec Filter (subBuffer: list<OutputChunk>, outputType: Option<Standard>) =
        match subBuffer with
        | [] -> new StringBuilder()
        | head::tail ->
            let filteredTail = Filter(tail, outputType)
            if (outputType.IsNone || head.OutputType = outputType.Value) then
                filteredTail.Append(head.Chunk.ToString())
            else
                filteredTail

    let rec Print (subBuffer: list<OutputChunk>): unit =
        match subBuffer with
        | [] -> ()
        | head::tail ->
            Print(tail)
            match head.OutputType with
            | Standard.Output ->
                Console.Write(head.Chunk.ToString())
                Console.Out.Flush()
            | Standard.Error ->
                Console.Error.Write(head.Chunk.ToString())
                Console.Error.Flush()

    member this.StdOut with get () = Filter(buffer, Some(Standard.Output)).ToString()

    member this.StdErr with get () = Filter(buffer, Some(Standard.Error)).ToString()

    member this.PrintToConsole() =
        Print(buffer)

    override self.ToString() =
        Filter(buffer, None).ToString()

type ProcessResult = { ExitCode: int; Output: OutputBuffer }

type ProcessDetails =
    { Command: string; Arguments: string }
    override self.ToString() =
        sprintf "Command: %s. Arguments: %s." self.Command self.Arguments

type Echo =
    | All
    | OutputOnly
    | Off

module Misc =

    let IsRunningInGitLab(): bool =
        let gitlabUserEmail = Environment.GetEnvironmentVariable "GITLAB_USER_EMAIL"
        not (String.IsNullOrEmpty gitlabUserEmail)

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

    let rec GatherOrGetDefaultPrefix(args: List<string>, previousIsPrefixArg: bool, prefixSet: Option<string>): string =
        let GatherPrefix(newPrefix: string): Option<string> =
            match prefixSet with
            | None -> Some newPrefix
            | _ -> failwith ("prefix argument duplicated")

        let prefixArgWithEquals = "--prefix="
        match args with
        | [] ->
            match prefixSet with
            | None -> "/usr/local"
            | Some prefix -> prefix
        | head::tail ->
            if previousIsPrefixArg then
                GatherOrGetDefaultPrefix(tail, false, GatherPrefix head)
            elif head = "--prefix" then
                GatherOrGetDefaultPrefix(tail, true, prefixSet)
            elif head.StartsWith prefixArgWithEquals then
                GatherOrGetDefaultPrefix(tail, false, GatherPrefix(head.Substring prefixArgWithEquals.Length))
            else
                failwithf "argument not recognized: %s" head


module Process =

    exception ProcessFailed of string

    type ProcessCouldNotStart(procDetails, innerException: Exception) =
        inherit Exception(sprintf
            "Process could not start! %s" (procDetails.ToString()),
            innerException)


    let Execute (procDetails: ProcessDetails, echo: Echo)
        : ProcessResult =

        // I know, this shit below is mutable, but it's a consequence of dealing with .NET's Process class' events?
        let outputBufferLock = new Object()
        let mutable outputBuffer: list<OutputChunk> = []

        if echo = Echo.All then
            Console.WriteLine(sprintf "%s %s" procDetails.Command procDetails.Arguments)
            Console.Out.Flush()

        let startInfo = new ProcessStartInfo(procDetails.Command, procDetails.Arguments)
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
                        let newBuilder = StringBuilder(leChar.ToString())
                        match outputBuffer with
                        | [] ->
                            let newBlock =
                                match std with
                                | Standard.Output ->
                                    { OutputType = Standard.Output; Chunk = newBuilder }
                                | Standard.Error ->
                                    { OutputType = Standard.Error; Chunk = newBuilder }
                            outputBuffer <- [ newBlock ]
                        | head::tail ->
                            match head.OutputType with
                            | Standard.Output ->
                                match std with
                                | Standard.Output ->
                                    head.Chunk.Append outChar |> ignore
                                | Standard.Error ->
                                    let newErrBuilder = { OutputType = Standard.Error; Chunk = newBuilder }
                                    outputBuffer <- newErrBuilder::outputBuffer
                            | Standard.Error ->
                                match std with
                                | Standard.Error ->
                                    head.Chunk.Append outChar |> ignore
                                | Standard.Output ->
                                    let newOutBuilder = { OutputType = Standard.Output; Chunk = newBuilder }
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
        | e -> raise(ProcessCouldNotStart(procDetails, e))

        outReaderThread.Start()
        errReaderThread.Start()
        proc.WaitForExit()
        let exitCode = proc.ExitCode

        outReaderThread.Join()
        errReaderThread.Join()

        { ExitCode = exitCode; Output = OutputBuffer(outputBuffer) }


    let CommandCheck (commandName: string): Option<string> =
        let commandWhich = Execute ({ Command = "which"; Arguments = commandName }, Echo.Off)
        if (commandWhich.ExitCode <> 0) then
            None
        else
            Some commandWhich.Output.StdOut


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
                    Some(Execute({ Command = "which"; Arguments = String.Empty }, Echo.Off))
                with
                | ex when (ExceptionIsOfTypeOrIncludesAnyInnerExceptionOfType(ex, typeof<System.ComponentModel.Win32Exception>))
                    -> None
                | _ -> reraise()

            match maybeResult with
            | None -> false
            | Some(_) -> true

        if not (WhichCommandWorksInShell()) then
            failwith "'which' doesn't work, please install it first"

        let whichProcResult = Execute({ Command = "which"; Arguments = command }, Echo.Off)
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


    let SafeExecute (procDetails: ProcessDetails, echo: Echo): ProcessResult =
        let procResult = Execute(procDetails, echo)
        if not (procResult.ExitCode = 0) then
            if (echo = Echo.Off) then
                Console.WriteLine procResult.Output.StdOut
                Console.Error.WriteLine procResult.Output.StdErr
                Console.Error.WriteLine()
            raise(ProcessFailed(sprintf "Command '%s' failed with exit code %d"
                                        procDetails.Command procResult.ExitCode))
        procResult


module Unix =

    let mutable firstTimeSudoIsRun = true
    let private SudoInternal (command: string, safe: bool): Option<int> =
        if not (Process.CommandWorksInShell "id") then
            Console.Error.WriteLine ("'id' unix command is needed for this script to work")
            Environment.Exit(2)

        let idOutput = Process.Execute({ Command = "id"; Arguments = "-u" }, Echo.Off).Output.StdOut
        let alreadySudo = (idOutput.Trim() = "0")

        if (alreadySudo && (not (Misc.IsRunningInGitLab()))) then
            Console.Error.WriteLine ("Error: sudo privileges detected. Please don't run this directly with sudo or with the root user.")
            Environment.Exit(3)

        if ((not (alreadySudo)) && (not (Process.CommandWorksInShell "sudo"))) then
            failwith "'sudo' unix command is needed for this script to work"

        if ((not (alreadySudo)) && firstTimeSudoIsRun) then
            Process.SafeExecute({ Command = "sudo"; Arguments = "-k" }, Echo.All) |> ignore

        if (not (alreadySudo)) then
            Console.WriteLine("Attempting sudo for '{0}'", command)

        // FIXME: we should change Sudo() signature to not receive command but command and args arguments
        let (commandToExecute,argsToPass) =
            match (alreadySudo) with
            | false -> ("sudo",command)
            | true ->
                if (command.Contains(" ")) then
                    let spaceIndex = command.IndexOf(" ")
                    let firstCommand = command.Substring(0, spaceIndex)
                    let args = command.Substring(spaceIndex + 1, command.Length - firstCommand.Length - 1)
                    (firstCommand,args)
                else
                    (command,String.Empty)

        let result =
            let cmd = { Command = commandToExecute; Arguments = argsToPass }
            if (safe) then
                Process.SafeExecute(cmd, Echo.All) |> ignore
                None
            else
                Some(Process.Execute(cmd, Echo.All).ExitCode)

        if (not (alreadySudo)) then
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

        let dpkgSearchProc = Process.Execute({ Command = "dpkg"; Arguments = sprintf "-s %s" packageName }, Echo.Off)
        if not (dpkgSearchProc.ExitCode = 0) then
            AptPackage.Missing
        else
            let dpkgLines = dpkgSearchProc.Output.StdOut.Split([|Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries)
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
        let procResult = Process.Execute({ Command = "apt"; Arguments = sprintf "download %s" packageName }, Echo.OutputOnly)
        if (procResult.ExitCode = 0) then
            Console.WriteLine("Downloaded " + packageName)
            ()
        else if (procResult.Output.StdErr.Contains("E: Can't select candidate version from package")) then
            Console.WriteLine()
            Console.WriteLine()
            Console.WriteLine("Virtual package '{0}' found, provided by:", packageName)
            InstallAptPackageIfNotAlreadyInstalled("aptitude")
            let aptitudeShowProc = Process.SafeExecute({ Command = "aptitude"; Arguments = sprintf "show %s" packageName }, Echo.Off)
            let lines = aptitudeShowProc.Output.StdOut.Split([|Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries)
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
        let aptRdependsProc = Process.SafeExecute({ Command = "apt-rdepends"; Arguments = packageName }, Echo.Off)
        let lines = aptRdependsProc.Output.StdOut.Split([|Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries)
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

module Git =

    let GetRepoInfo () =
        let rec GetBranchFromGitBranch(outchunks: List<string>) =
            match outchunks with
            | [] -> failwith "current branch not found, unexpected output from `git branch`"
            | head::tail ->
                if head.StartsWith "*" then
                    let branchName = head.Substring("* ".Length)
                    branchName
                else
                    GetBranchFromGitBranch tail

        let gitWhich = Process.Execute({ Command = "which"; Arguments = "git" }, Echo.Off)
        if gitWhich.ExitCode <> 0 then
            String.Empty
        else
            let gitLog = Process.Execute({ Command = "git"; Arguments = "log --oneline" }, Echo.Off)
            if gitLog.ExitCode <> 0 then
                String.Empty
            else
                let gitBranch = Process.Execute({ Command = "git"; Arguments = "branch" }, Echo.Off)
                if gitBranch.ExitCode <> 0 then
                    failwith "Unexpected git behaviour, as `git log` succeeded but `git branch` didn't"
                else
                    let branchesOutput =
                        gitBranch.Output.StdOut.Split([|Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries)
                            |> List.ofSeq
                    let branch = GetBranchFromGitBranch branchesOutput
                    let gitLogCmd = { Command = "git"
                                      Arguments = "log --no-color --first-parent -n1 --pretty=format:%h" }
                    let gitLastCommit = Process.Execute(gitLogCmd, Echo.Off)
                    if gitLastCommit.ExitCode <> 0 then
                        failwith "Unexpected git behaviour, as `git log` succeeded before but not now"

                    let lines = gitLastCommit.Output.StdOut.Split([|Environment.NewLine|],
                                                                  StringSplitOptions.RemoveEmptyEntries)
                    if lines.Length <> 1 then
                        failwith "Unexpected git output for special git log command"
                    else
                        let lastCommitSingleOutput = lines.[0]
                        sprintf "(%s/%s)" branch lastCommitSingleOutput

