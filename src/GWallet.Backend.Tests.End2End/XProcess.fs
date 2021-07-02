namespace GWallet.Backend.Tests.End2End

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.IO
open System.Security
open System.Threading

open GWallet.Backend.FSharpUtil.UwpHacks

[<RequireQualifiedAccess>]
module XProcess =

    /// A cross-platform process.
    type XProcess =
        private
            {
                Process: Process
                Output: ConcurrentQueue<string>
                Semaphore: Semaphore
            }

    let private CreateOutputReceiver
        processName
        processId
        (firstOutputTerminated: Ref<bool>)
        (output: ConcurrentQueue<string>)
        (semaphore: Semaphore) =

        // lambda for actual handler
        fun (_: obj) (args: DataReceivedEventArgs) ->

            // NOTE: we need to wait for both streams (stdout and stderr) to end.
            // So output has ended and the process has exited after the second null.
            lock firstOutputTerminated <| fun () ->
                match args.Data with
                | null ->
                    if not !firstOutputTerminated then
                        firstOutputTerminated := true
                    else
                        printfn "%s (%i) <exited>" processName processId
                        semaphore.Release () |> ignore<int>
                | text ->
                    printfn "%s (%i): %s" processName processId text
                    output.Enqueue text
                    semaphore.Release () |> ignore<int>

    let private CreateOSProcess processPath processArgs (processEnv: Map<string, string>) credentialsOpt =

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
            let passwordSecure = new SecureString ()
            for c in password do passwordSecure.AppendChar c
            processStartInfo.UserName <- userName
            processStartInfo.Password <- passwordSecure
        | None -> ()

        // populate process start info environment vars
        for kvp in processEnv do
            processStartInfo.Environment.[kvp.Key] <- kvp.Value

        // create process object and tie its lifetime to spawning process's
        let processInstance = new Process (StartInfo = processStartInfo, EnableRaisingEvents = true)
        AppDomain.CurrentDomain.ProcessExit.AddHandler (EventHandler (fun _ _ -> processInstance.Close ()))
        processInstance

    let private RedirectProcessOutput processName processOutput (processInstance: Process) semaphore =
        let firstOutputTerminated = ref false
        let outputReceiver = CreateOutputReceiver processName processInstance.Id firstOutputTerminated processOutput semaphore
        processInstance.OutputDataReceived.AddHandler (DataReceivedEventHandler outputReceiver)
        processInstance.ErrorDataReceived.AddHandler (DataReceivedEventHandler outputReceiver)
        processInstance.BeginOutputReadLine ()
        processInstance.BeginErrorReadLine ()

    let private TryDiscoverProcessPath processName (envPath: string) =
        let envPaths = envPath.Split Path.PathSeparator
        let processPaths = [for path in envPaths do yield Path.Combine (path, processName)]
        List.tryFind File.Exists processPaths

    /// Check that that process has exited.
    let HasExited xprocess =
        xprocess.Process.HasExited

    /// Wait the process's messages until one is found that satisfies the given filter.
    let rec WaitForMessage (msgFilter: string -> bool) xprocess =
        xprocess.Semaphore.WaitOne () |> ignore<bool>
        let (running, line) = xprocess.Output.TryDequeue ()
        if running then
            printfn "%s" line
            if not (msgFilter line) then
                WaitForMessage msgFilter xprocess
        else
            failwith (xprocess.Process.ProcessName + " exited without outputting message.")

    /// Wait for the process to exit, killing it if requested.
    let rec WaitForExit kill xprocess =
        if kill && not xprocess.Process.HasExited then xprocess.Process.Kill ()
        xprocess.Semaphore.WaitOne () |> ignore
        let (running, _) = xprocess.Output.TryDequeue ()
        if running then WaitForExit false xprocess

    /// Read all of the process's existing messages.
    let ReadMessages xprocess: list<string> =
        let rec readMessages (lines: list<string>) =
            xprocess.Semaphore.WaitOne () |> ignore<bool>
            let (running, line) = xprocess.Output.TryDequeue ()
            if running then readMessages (List.append lines [line])
            else lines
        readMessages List.empty

    /// Skip all of the process's existing messages.
    let rec SkipMessages xprocess =
        xprocess.Semaphore.WaitOne () |> ignore<bool>
        let (running, _) = xprocess.Output.TryDequeue ()
        if running then SkipMessages xprocess

    /// Start a process based on the execution environment.
    let Start processName processArgs processEnv =

        // construct a semaphore to synchronize process output handling
        // TODO: document exactly how this works!
        // TODO: see if we can find a more understandable way to implement this (AutoResetEvent?)
        let semaphore = new Semaphore (0, Int32.MaxValue)

        // when in *nix, do as the *nix
        if Environment.OSVersion.Platform = PlatformID.Unix then
        
            // attempt to discover process path
            let envPath = Environment.GetEnvironmentVariable "PATH"
            let processPath =
                match TryDiscoverProcessPath processName envPath with
                | Some processPath -> processPath
                | None -> failwithf "Could not find process file %s in $PATH=%s" processName envPath

            // attempt to create and start OS process
            let processInstance = CreateOSProcess processPath processArgs processEnv None
            let processStarted = processInstance.Start ()
            if not processStarted then
                failwithf "Failed to start process for %s." processPath
            
            // redirect process output to spawning process's
            let processOutput = ConcurrentQueue ()
            RedirectProcessOutput processName processOutput processInstance semaphore

            // make XProcess
            { Process = processInstance; Output = processOutput; Semaphore = semaphore }

        else // otherwise windows
        
            // construct process path
            let processPath = "wsl.exe"

            // attempt to grab wsl credentials from file
            let credentialsFilePath = "../../../WslCredentials.dat"
            let processCredentials =
                if File.Exists credentialsFilePath then
                    let credentialsText = File.ReadAllText credentialsFilePath
                    match credentialsText.Split ([|Environment.NewLine|], StringSplitOptions.None) with
                    | [|userName; password|] -> (userName, password)
                    | _ -> failwithf "Expecting only user name and password in %s." credentialsFilePath
                else failwithf "Cannot find Windows Subsystem for Linux credentials file at %s." credentialsFilePath

            // attempt to create and start OS process
            let processArgsPlus = SPrintF2 "%s %s" processName processArgs
            let processInstance = CreateOSProcess processPath processArgsPlus processEnv (Some processCredentials)
            let processStarted = processInstance.Start ()
            if not processStarted then
                failwithf "Failed to start process for %s." processPath

            // redirect process output to spawning process's
            let processOutput = ConcurrentQueue ()
            RedirectProcessOutput processName processOutput processInstance semaphore

            // make XProcess
            { Process = processInstance; Output = processOutput; Semaphore = semaphore }

/// Forward XProcess type to containing namespace.
/// NOTE: In newer versions of F#, we need only utter `type XProcess = XProcess.XProcess`, but because we're
/// supporting an older version, we need this forwarding module as well.
[<AutoOpen>]
module XProcessAuto =

    /// A cross-platform process.
    type XProcess = XProcess.XProcess