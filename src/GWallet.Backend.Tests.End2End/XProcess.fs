namespace GWallet.Backend.Tests.End2End

open System
open System.IO
open System.Diagnostics
open System.Collections.Concurrent

[<RequireQualifiedAccess; CompilationRepresentation (CompilationRepresentationFlags.ModuleSuffix)>]
module XProcess =

    /// A cross-platform process.
    type XProcess =
        private
            { Process : Process
              Output : ConcurrentQueue<string>
            }

    let private CreateOutputReceiver processName processId (firstOutputTerminated: Ref<bool>) (output: ConcurrentQueue<string>) =

        // lambda for actual handler
        fun (_: obj) (args: DataReceivedEventArgs) ->

            // NOTE: we need to wait for both streams (stdout and stderr) to end.
            // So output has ended and the process has exited after the second null.
            lock firstOutputTerminated <| fun () ->
                match args.Data with
                | null ->
                    if not !firstOutputTerminated
                    then firstOutputTerminated := true
                    else printf "%s (%i) <exited>" processName processId
                | text ->
                    printf "%s (%i): %s" processName processId text
                    output.Enqueue text

    let private CreateProcess processPath processArgs (processEnv: Map<string, string>) credentialsOpt =

        // make process start info
        let processStartInfo =
            ProcessStartInfo (
                UseShellExecute = false,
                FileName = processPath,
                Arguments = processArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            )

        // populate credential if available
        match credentialsOpt with
        | Some (userName, password) ->
            processStartInfo.UserName <- userName
            processStartInfo.Password <- let pwd = new Security.SecureString () in (for c in password do pwd.AppendChar c); pwd
        | None -> ()

        // populate process start info environment vars
        for kvp in processEnv do
            processStartInfo.Environment.[kvp.Key] <- kvp.Value

        // create process object
        let processInstance = new Process (StartInfo = processStartInfo, EnableRaisingEvents = true)

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
    let Start processName processArgs processEnv =

        if Environment.OSVersion.Platform = PlatformID.Unix then
        
            // attempt to discover process path
            let envPath = Environment.GetEnvironmentVariable "PATH"
            let processPath =
                match TryDiscoverProcessPath processName envPath with
                | Some processPath -> processPath
                | None -> failwithf "Could not find process file %s in $PATH=%s" processName envPath

            // attempt to create and start OS process
            let processInstance = CreateProcess processPath processArgs processEnv None
            let processStarted = processInstance.Start ()
            if not processStarted then
                failwithf "Failed to start process for %s." processPath
            
            // stream process's output to spawning process's
            let firstOutputTerminated = ref false
            let output = ConcurrentQueue ()
            let outputReceiver = CreateOutputReceiver processName processInstance.Id firstOutputTerminated output
            processInstance.OutputDataReceived.AddHandler (DataReceivedEventHandler outputReceiver)
            processInstance.ErrorDataReceived.AddHandler (DataReceivedEventHandler outputReceiver)
            processInstance.BeginOutputReadLine ()
            processInstance.BeginErrorReadLine ()

            // make linux process
            let xProcess = { Process = processInstance; Output = output }
            xProcess

        else // assume windows
        
            // construct process path
            let processPath = "wsl.exe"

            // attempt to create and start OS process
            let processArgsPlus = sprintf "%s %s" processName processArgs
            let processCredentials = ("USER_NAME", "PASSWORD") // TODO: populate these somehow
            let processInstance = CreateProcess processPath processArgsPlus processEnv (Some processCredentials)
            let processStarted = processInstance.Start ()
            if not processStarted then
                failwithf "Failed to start process for %s." processPath
            
            // stream process's output to spawning process's
            let firstOutputTerminated = ref false
            let output = ConcurrentQueue ()
            let outputReceiver = CreateOutputReceiver processName processInstance.Id firstOutputTerminated output
            processInstance.OutputDataReceived.AddHandler (DataReceivedEventHandler outputReceiver)
            processInstance.ErrorDataReceived.AddHandler (DataReceivedEventHandler outputReceiver)
            processInstance.BeginOutputReadLine ()
            processInstance.BeginErrorReadLine ()

            // make wsl process
            let xProcess = { Process = processInstance; Output = output }
            xProcess

    /// Kill the process.
    let Kill xprocess =
        if not xprocess.Process.HasExited then xprocess.Process.Kill ()
        SkipMessages xprocess

/// A cross-platform process.
type XProcess = XProcess.XProcess