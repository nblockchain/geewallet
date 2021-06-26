namespace GWallet.Backend.Tests.End2End

open System
open System.IO
open System.Diagnostics
open System.Collections.Concurrent

[<RequireQualifiedAccess>]
module XProcess =

    /// A cross-platform process.
    type XProcess =
        private
            { Process : Process
              Output : ConcurrentQueue<string>
            }

    let private CreateOutputReceiver processName processId (output: ConcurrentQueue<string>) (firstStreamEnded: Ref<bool>) =

        // lambda for actual handler
        fun (_: obj) (args: DataReceivedEventArgs) ->

            // NOTE: we need to wait for both streams (stdout and stderr) to end.
            // So output has ended and the process has exited after the second null.
            lock firstStreamEnded <| fun () ->
                match args.Data with
                | null ->
                    if not !firstStreamEnded
                    then firstStreamEnded := true
                    else printf "%s (%i) <exited>" processName processId
                | text ->
                    printf "%s (%i): %s" processName processId text
                    output.Enqueue text

    let private CreateProcess processName processPath processArgs (processEnv: Map<string, string>) output =

        // make process start info
        let processStartInfo =
            ProcessStartInfo (
                UseShellExecute = false,
                FileName = processPath,
                Arguments = processArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            )

        // populate process start info environment vars
        for kvp in processEnv do
            processStartInfo.Environment.[kvp.Key] <- kvp.Value

        // create process object
        let processInstance = new Process (StartInfo = processStartInfo, EnableRaisingEvents = true)

        // stream process's output to spawning process's
        let firstStreamEnded = ref false
        let outputReceiver = CreateOutputReceiver processName processInstance.Id output firstStreamEnded
        processInstance.OutputDataReceived.AddHandler (DataReceivedEventHandler outputReceiver)
        processInstance.ErrorDataReceived.AddHandler (DataReceivedEventHandler outputReceiver)
        processInstance.BeginOutputReadLine () // NOTE: this may need to be called after starting the OS process!
        processInstance.BeginErrorReadLine ()

        // tie process lifetime to spawning process's
        AppDomain.CurrentDomain.ProcessExit.AddHandler (EventHandler (fun _ _ -> processInstance.Close ()))
        processInstance

    let private TryDiscoverProcessPath processName (envPath: string) =
        let envPaths = envPath.Split Path.PathSeparator
        let processPaths = [for path in envPaths do yield Path.Combine (path, processName)]
        List.tryFind File.Exists processPaths

    /// Check that that process has exited.
    let HasExited xprocess =
        xprocess.Process.HasExited

    /// Skip the process's existing message until one is found that satisfies the given filter.
    let rec SkipToMessage (msgFilter: string -> bool) xprocess =
        let (running, line) = xprocess.Output.TryDequeue ()
        if running then
            if not (msgFilter line) then
                SkipToMessage msgFilter xprocess
        else
            failwith (xprocess.Process.ProcessName + " exited without outputting message.")

    /// Skip all of the process's existing messages.
    let rec SkipMessages xprocess =
        let (running, _) = xprocess.Output.TryDequeue ()
        if running then SkipMessages xprocess

    /// Read all of the process's existing messages.
    let ReadMessages xprocess: list<string> =
        let rec readMessages (lines: list<string>) =
            let (running, line) = xprocess.Output.TryDequeue ()
            if running then readMessages (List.append lines [line])
            else lines
        readMessages List.empty

    /// Start a process based on the execution environment.
    let Start processName processArgs processEnv isPython =

        if Environment.OSVersion.Platform = PlatformID.Unix then
        
            // attempt to discover process path
            let envPath = Environment.GetEnvironmentVariable "PATH"
            let processPath =
                match TryDiscoverProcessPath processName envPath with
                | Some processPath -> processPath
                | None -> failwithf "Could not find process file %s in $PATH=%s" processName envPath

            // attempt to start OS process
            let processOutput = ConcurrentQueue ()
            let processInstance = CreateProcess processName processPath processArgs processEnv processOutput
            let processStarted = processInstance.Start ()
            if not processStarted then
                failwithf "Failed to start process for %s." processPath

            // make linux process
            let xProcess = { Process = processInstance; Output = processOutput }
            xProcess

        else // assume windows
        
            // attempt to discover process path
            let envPath = Environment.GetEnvironmentVariable "PATH"
            let processNameExt = processName + if isPython then ".py" else ".exe"
            let processPath =
                match TryDiscoverProcessPath processNameExt envPath with
                | Some processPath -> processPath
                | None -> failwithf "Could not find process file %s in $PATH=%s" processName envPath

            // attempt to start OS process
            let processOutput = ConcurrentQueue ()
            let processInstance = CreateProcess processName processPath processArgs processEnv processOutput
            let processStarted = processInstance.Start ()
            if not processStarted then
                failwithf "Failed to start process for %s." processPath

            // make wsl process
            let xProcess = { Process = processInstance; Output = processOutput }
            xProcess

    /// Kill the process.
    let Kill xprocess =
        xprocess.Process.Kill ()
        SkipMessages xprocess

/// A cross-platform process.
type XProcess = XProcess.XProcess