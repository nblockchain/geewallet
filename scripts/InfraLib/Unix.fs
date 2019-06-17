
namespace FSX.Infrastructure

open System
open System.IO
open System.Linq

open Process

module Unix =

    let AbortIfNotDistroAndVersion(distro: string, version: string) =
        let procResult = Process.SafeExecute({ Command = "lsb_release"; Arguments = "-a" }, Echo.Off)
        let tsvMap = Misc.TsvParse(procResult.Output.StdOut)
        let thisDistro = tsvMap.TryFind("Distributor ID:")

        let err = ("Invalid OS/distro, this script is prepared for {0} {1} only", distro, version)

        match thisDistro with
        | None -> failwith("Strangely your OS doesn't contain standard values for `lsb_release -a`")
        | Some(distName) ->
            if not (distName = distro) then
                Console.Error.WriteLine err
                Environment.Exit(1)

        let thisVersion = tsvMap.TryFind("Release:")
        match thisVersion with
        | None -> failwith("Strangely your OS doesn't contain standard values for `lsb_release -a`")
        | Some(distVersion) ->
            if not (distVersion = version) then
                Console.Error.WriteLine err
                Environment.Exit(2)

    // NOTE: System.Diagnostics.Process.GetProcesses[ByName](...) only returns the ones running under the current user
    let RunningProcessesFromAllUsers() =
        let procResult = Process.Execute({ Command = "ps"; Arguments = "aux"}, Echo.Off)
        let procs = List.ofSeq(procResult.Output.StdOut.Split([|Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries))
        let processes = match procs with
                        | headerRow::processRows -> processRows
                        | _ -> failwith "Unexpected data from 'ps'"
        [
            for proc in processes do
                if (proc = null) then
                    failwith ("Nuts, single proc from processes was null")
                let procElements: string array = proc.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                if (procElements = null) then
                    failwith ("Nuts, procElements was null")
                yield procElements
        ]

    // (there's no native way of doing this in .NET...: http://stackoverflow.com/questions/777548/how-do-i-determine-the-owner-of-a-process-in-c )
    let RunningProcessesFromUser(username: string) =
        if (String.IsNullOrWhiteSpace(username)) then
            raise (ArgumentException("username cannot be blank", "username"))

        seq {
            for procElements in RunningProcessesFromAllUsers() do
                if (username.Equals(procElements.[0])) then
                    yield procElements
        } |> List.ofSeq

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

    let MAX_TIME_TO_WAIT_FOR_A_PROCESS_TO_DIE = TimeSpan.FromSeconds(float 30)
    let KillAll(processName: string) =
        let mutable killallStarted: Option<DateTime> = None
        while (RunningProcessesFromAllUsers().Any(fun procElements -> procElements.Any(fun elt -> elt = processName))) do
            if (killallStarted = None
                || (DateTime.Now < killallStarted.Value + MAX_TIME_TO_WAIT_FOR_A_PROCESS_TO_DIE)) then
                if (killallStarted = None) then
                    killallStarted <- Some(DateTime.Now)
                else
                    Console.WriteLine("Retrying...")
                try
                    Sudo("killall " + processName)
                with
                | Process.ProcessFailed(_) -> () //might fail if the process is gone already, it's fine
                System.Threading.Thread.Sleep(TimeSpan.FromSeconds(float 1))
            else
                let dashNine = String.Format("You might consider using 'killall -9 {0}' manually, but this is kind of dangerous." +
                                             Environment.NewLine +
                                             "(At least make sure to `tail -f` the logs first to make sure they are really stalled?))",
                                             processName)
                failwith (String.Format("Process {0} refuses to die after {1} seconds. {2}",
                                        processName,
                                        MAX_TIME_TO_WAIT_FOR_A_PROCESS_TO_DIE.TotalSeconds,
                                        dashNine))

        if (killallStarted = None) then
            Console.WriteLine("No process '{0}' found, skipping.", processName)
        else
            Console.WriteLine("Successfully killed '{0}'", processName)

    let private ArgsForBash(commandWithArguments: string) =
        String.Format("-c \"{0}\"", commandWithArguments.Replace("\"", "\\\""))

    let ExecuteBashCommand (commandWithArguments: string, echo: Echo) =
        let args = ArgsForBash(commandWithArguments)
        Process.Execute({ Command = "bash"; Arguments = args }, echo)

    let SafeExecuteBashCommand (commandWithArguments: string, echo: Echo) =
        let args = ArgsForBash(commandWithArguments)
        Process.SafeExecute({ Command = "bash"; Arguments = args }, echo)

    type AptPackage =
    | Missing
    | ExistingVersion of string
    let IsAptPackageInstalled(packageName: string): AptPackage =
        if not (Process.CommandWorksInShell "dpkg") then
            Console.Error.WriteLine ("This script is only for debian-based distros, aborting.")
            Environment.Exit(3)

        let cmd = { Command = "dpkg"; Arguments = String.Format("-s {0}", packageName) }
        let procResult = Process.Execute(cmd, Echo.Off)
        if not (procResult.ExitCode = 0) then
            AptPackage.Missing
        else
            let dpkgLines = procResult.Output.StdOut.Split([|Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries)
            let versionTag = "Version: "
            let maybeVersion = dpkgLines.Where(fun line -> line.StartsWith(versionTag)).Single()
            let version = maybeVersion.Substring(versionTag.Length)
            AptPackage.ExistingVersion(version)

    let InstallAptPackageIfNotAlreadyInstalled(packageName: string)=
        if (IsAptPackageInstalled(packageName) = AptPackage.Missing) then
            Console.WriteLine("Installing {0}...", packageName)
            Sudo(String.Format("apt -y install {0}", packageName)) |> ignore

    let InstallDebPackage(package: FileInfo)=
        if not (Process.CommandWorksInShell "dpkg") then
            Console.Error.WriteLine ("This script is only for debian-based distros, aborting.")
            Environment.Exit(3)

        Console.WriteLine("Installing {0}...", package.Name)
        Sudo(String.Format("dpkg --install {0}", package.FullName)) |> ignore

    let OctalPermissions (fileOrDir: FileSystemInfo): int =
        let cmd = { Command = "stat"; Arguments = String.Format("-c \"%a\" {0}", fileOrDir.FullName) }
        let output = Process.SafeExecute(cmd, Echo.Off).Output.StdOut
        Int32.Parse(output.Trim())

    type Server =
    | ThisServer
    | RemoteServer of string

    let private GrabTheFirstStringBeforeTheFirstColon (lines: seq<string>) =
        seq {
            for line in (lines) do
                yield (line.Split([|":"|], StringSplitOptions.RemoveEmptyEntries)).[0]
        }

    let GetUsersInTheSystem (server: Server) =
        let usersFile = "/etc/passwd"
        let lines: seq<string> =
            match server with
            | ThisServer -> File.ReadLines(usersFile)
            | RemoteServer(address) ->
                let cmd = { Command = "ssh"; Arguments = String.Format("-t {0} cat /etc/passwd", address) }
                let output = Process.Execute(cmd, Echo.Off).Output.StdOut
                output.Split([| Environment.NewLine |], StringSplitOptions.RemoveEmptyEntries) |> Seq.ofArray
        GrabTheFirstStringBeforeTheFirstColon(lines)

    let GetGroupsInTheSystem () =
        let cmd = { Command = "getent"; Arguments = "group" }
        let output = Process.SafeExecute(cmd, Echo.Off).Output.StdOut
        let lines = output.Split([| Environment.NewLine |], StringSplitOptions.RemoveEmptyEntries)
        GrabTheFirstStringBeforeTheFirstColon(lines)

    let AddUserToGroup (username: string, group: string) =
        // FIXME: rename to AddUserToGroupIfNotPartOfIt , and use `groups username` to check
        let addUserToGroupCommand = String.Format("sudo usermod -a -G {0} {1}", group, username)
        Sudo(addUserToGroupCommand)

    let AddGroupIfNotExistent (groupname: string) =
        if (Seq.exists(fun group -> group.Equals(groupname)) (GetGroupsInTheSystem())) then
            ()
        else
            let addGroupCommand = String.Format("addgroup {0}", groupname)
            Sudo(addGroupCommand)

    let AddUserIfNotExistent (username: string, server: Server) =
        let addUserCommand = String.Format("adduser --gecos \"\" --disabled-password {0}", username)
        if (Seq.exists(fun user -> user.Equals(username)) (GetUsersInTheSystem(server))) then
            ()
        else
            match server with
            | ThisServer -> Sudo(addUserCommand)
            | RemoteServer(address) ->
                let cmd = { Command = "ssh"; Arguments = String.Format("-t {0} 'sudo {1}'", address, addUserCommand) }
                Process.SafeExecute(cmd, Echo.All) |> ignore

    let SudoFileExists (path: string): bool =
        let cmd = { Command = "sudo"; Arguments = String.Format("stat -c \"\" {0}", path) }
        let exitCode = Process.Execute(cmd, Echo.Off).ExitCode
        (exitCode = 0)

    let AddRemoteServerFingerprintsToLocalKnownHostsFile(server: string, username: string) =
        let home =
            if (username = "root") then
                "/root"
            else
                Path.Combine("/home", username)

        let sshFolder = Path.Combine(home, ".ssh")
        Sudo(String.Format("runuser -l {0} -c 'mkdir -p {1}'", username, sshFolder))
        let knownHostsFilePath = Path.Combine(sshFolder, "known_hosts")

        if not (SudoFileExists(knownHostsFilePath)) then
            Sudo(String.Format("runuser -l {0} -c 'touch {1}'", username, knownHostsFilePath))

        let command1 = String.Format("ssh-keygen -R {0}", server)
        let command2 = String.Format("ssh-keyscan -H {0} >> {1}", server, knownHostsFilePath)

        Sudo(String.Format("runuser -l {0} -c '{1}'", username, command1))
        Sudo(String.Format("runuser -l {0} -c '{1}'", username, command2))

    let GetOwner(fileOrDirectory: FileSystemInfo): string =
        let cmd = { Command = "stat"; Arguments = String.Format("-c \"%U\" {0}", fileOrDirectory.FullName) }
        let output = Process.SafeExecute(cmd, Echo.Off).Output.StdOut
        output.Trim()

    let ChangeOwner (fileOrDirectory: FileSystemInfo, newOwner: string, recursively: bool): unit =
        let recursiveStr =
            if (recursively) then
                "-R"
            else
                String.Empty
        if (recursively || not (newOwner.Equals(GetOwner(fileOrDirectory)))) then
            Sudo(String.Format("chown {0} {1} {2}", recursiveStr, newOwner, fileOrDirectory.FullName))

    let ChangeOwnerUserAndOwnerGroup (fileOrDirectory: FileSystemInfo, newOwner: string, newGroup: string, recursively: bool): unit =
        let recursiveStr =
            if (recursively) then
                "-R"
            else
                String.Empty
        if (recursively || not (newOwner.Equals(GetOwner(fileOrDirectory)))) then
            Sudo(String.Format("chown {0} {1}:{2} {3}", recursiveStr, newOwner, newGroup, fileOrDirectory.FullName))

    let ChangeMode (fileOrDirectory: FileSystemInfo, mode: string, recursively: bool): unit =
        let recursiveStr =
            if (recursively) then
                "-R"
            else
                String.Empty
        Sudo(String.Format("chmod {0} {1} {2}", recursiveStr, mode, fileOrDirectory.FullName))

    let ShellIsEnabledForUserOnThisServer(username: string) =
        let someRandomCommandWhichShouldWork = "exit"
        let exitCode = UnsafeSudo(String.Format("runuser -l {0} -c '{1}'",
                                                 username, someRandomCommandWhichShouldWork))
        (exitCode = 0)

    let ChangeShellForUser(username: string, shell: string, server: Server) =
        //FIXME: should parse /etc/passwd first to see if it's needed or already done
        let command = String.Format("chsh -s {0} {1}", shell, username)
        match server with
        | Server.ThisServer -> Sudo(String.Format(command, username))
        | Server.RemoteServer(address) ->
            let cmd = { Command = "ssh"; Arguments = String.Format("-t {0} sudo {1}", address, command) }
            Process.SafeExecute(cmd, Echo.All) |> ignore

    let EnableShellForUser(username: string, server: Server) =
        ChangeShellForUser(username, "/bin/bash", server)

    let DisableShellForUser(username: string, server: Server) =
        ChangeShellForUser(username, "/usr/sbin/nologin", server)

    let WhoAmI(): string =
        let cmd = { Command = "whoami"; Arguments = String.Empty }
        let whoAmIoutput = Process.SafeExecute(cmd, Echo.Off).Output.StdOut
        whoAmIoutput.Trim()

    let SetupPasswordLessLogin(host: string, user: string) =
        Console.WriteLine("Passwordless login is not enabled yet in the remote host, going to set it up...")
        AddUserIfNotExistent(user, Server.ThisServer)
        AddUserIfNotExistent(user, Server.RemoteServer(host))
        //EnableShellForUser(user, Server.RemoteServer(host))

        if not (ShellIsEnabledForUserOnThisServer(user)) then
            EnableShellForUser(user, Server.ThisServer)

        let dotSshFolder = Path.Combine("/home", user, ".ssh")
        let privateKeyRelativePath = Path.Combine(dotSshFolder, "id_rsa")
        let publicKeyName = "id_rsa.pub"
        let publicKeyRelativePath = Path.Combine(dotSshFolder, publicKeyName)
        if not (SudoFileExists(privateKeyRelativePath)) then
            Console.WriteLine("File {0} doesn't exist!", privateKeyRelativePath)
            let nonInteractiveSshKeyGenCommand = String.Format("ssh-keygen -t rsa -N \"\" -f {0}", privateKeyRelativePath)
            Sudo(String.Format("runuser -l {0} -c '{1}'",
                                        user, nonInteractiveSshKeyGenCommand))

        let currentUser = WhoAmI()
        AddRemoteServerFingerprintsToLocalKnownHostsFile(host, currentUser)
        AddRemoteServerFingerprintsToLocalKnownHostsFile(host, user)
        AddRemoteServerFingerprintsToLocalKnownHostsFile(host, "root") //for sudo

        Sudo(String.Format("scp {0} {1}@{2}:~/", publicKeyRelativePath, currentUser, host))

        let authorizedKeysFilePath = Path.Combine(dotSshFolder, "authorized_keys")
        let firstCommandsInBackupNode = [
                                          String.Format("sudo mkdir -p {0}", dotSshFolder);
                                          String.Format("sudo mv {0} {1}", publicKeyName, authorizedKeysFilePath);
                                          String.Format("sudo chown -R {0}:{0} {1}", user, dotSshFolder);
                                          String.Format("sudo chmod 700 {0}", dotSshFolder);
                                          String.Format("sudo chmod 640 {0}", authorizedKeysFilePath);
                                        ]
        let cmd = { Command = "ssh"; Arguments = String.Format("-t {0} '{1}'",
                                                               host,
                                                               String.Join(" && ", firstCommandsInBackupNode)) }
        Process.SafeExecute(cmd, Echo.All) |> ignore

    let private NeedsSudo(user: string): bool =
        if (WhoAmI() = user) then
            false
        else
            true

    // TODO: split this in two overloads, the one without requireSudo should not be in Unix
    let CopyFileIfNewer (source: FileInfo, dest: DirectoryInfo, requireSudo: bool) =
        let destFile = new FileInfo(Path.Combine(dest.FullName, source.Name))
        let copyFile =
            if not (destFile.Exists) then
                true
            else
                (destFile.LastWriteTime < source.LastWriteTime)
        if (copyFile) then
            if (requireSudo) then
                Sudo(String.Format("cp {0} {1}", source, dest)) |> ignore
            else
                source.CopyTo(destFile.FullName, true) |> ignore
        copyFile,destFile

    // TODO: split this in two overloads, the one without requireSudo should not be in Unix
    let MoveFileIfNewer (source: FileInfo, dest: FileInfo, requireSudo: bool): bool =
        let destAlreadyExists = dest.Exists
        let moveFile =
            if not (destAlreadyExists) then
                true
            else
                (dest.LastWriteTime < source.LastWriteTime)
        if not (moveFile) then
            source.Delete()
        else
            if (requireSudo) then
                Sudo(String.Format("mv {0} {1}", source, dest))
            else
                if (destAlreadyExists) then
                    dest.Delete()
                File.Move(source.FullName, dest.FullName)
        moveFile

    type SshClient =
    | ErrorConnecting
    | Address of string

    let PasswordLessLoginWorksOnRemoteStorageBackupHost (host: string, user: string): SshClient =
        let needsSudo = NeedsSudo(user)

        let tryEnablingShell: bool =
            try
                if (needsSudo && (not (ShellIsEnabledForUserOnThisServer(user)))) then
                    EnableShellForUser(user, Server.ThisServer)
                true
            with
                // because it might spit:
                //  chsh: user 'backupdb' does not exist
                //  System.Exception: Command 'sudo' failed with exit code 1. Arguments supplied: 'chsh -s /bin/bash backupdb'
                | _ -> false

        if not (tryEnablingShell) then
            SshClient.ErrorConnecting
        else
            let getAddressFromSshClientInSshSession = "echo $SSH_CLIENT"

            let procResult =
                try
                    let sshArguments = String.Format("-o BatchMode=yes {0} \'{1}'", host, getAddressFromSshClientInSshSession)
                    if (needsSudo) then
                        let cmd = { Command = "sudo"; Arguments = String.Format("runuser -l {0} -c \"ssh {1}\"", user, sshArguments) }
                        Process.Execute(cmd, Echo.Off)
                    else
                        Process.Execute({ Command = "ssh"; Arguments = sshArguments }, Echo.Off)

                finally
                    if (needsSudo) then
                        DisableShellForUser(user, Server.ThisServer)

            if not (procResult.ExitCode = 0) then
                Console.WriteLine(procResult.Output.StdOut)
                Console.Error.WriteLine(procResult.Output.StdErr)
                SshClient.ErrorConnecting
            else
                SshClient.Address(procResult.Output.StdOut.Split(' ').[0])


    let ListCrontab (username: string) =
        let needsSudo = NeedsSudo(username)

        if (needsSudo) then
            let cmd = { Command = "sudo"; Arguments = String.Format("crontab -u {0} -l", username) }
            Process.Execute(cmd, Echo.OutputOnly) |> ignore
        else
            Process.Execute({ Command = "crontab"; Arguments = "-l" }, Echo.OutputOnly) |> ignore

    let private ConvertCommandToCronJob (command: string, args: string, minutes: int): string =
        String.Format("*/{0} * * * * {1} {2}", minutes.ToString(), command, args)

    let private ReplaceCommandInCronJob (cronJob: string, command: string, args: string, minutes: int): Option<string * bool> =
        if (String.IsNullOrWhiteSpace(cronJob)) then
            None
        else
            if not (cronJob.Trim().Contains(command)) then
                Some(cronJob, false)
            else
                let commandReplaced = ConvertCommandToCronJob(command, args, minutes)
                Console.WriteLine ("Replaced '{0}' with '{1}' in crontab", cronJob, commandReplaced)
                Some(commandReplaced, true)

    let rec private ReplaceCommandInCrontabJobs (cronJobs: string list, command: string, args: string, minutes: int, found: bool): string list =
        match cronJobs with
        | [] ->
            if not (found) then
                [ ConvertCommandToCronJob(command, args, minutes) ]
            else
                []
        | someCronJob::tail ->
            let replacedCronJob = ReplaceCommandInCronJob(someCronJob, command, args, minutes)
            match replacedCronJob with
            | None ->
                tail
            | Some(cronJobMaybeReplaced,replaced) ->
                if (found) then
                    ReplaceCommandInCrontabJobs(tail, command, args, minutes, true)
                else
                    cronJobMaybeReplaced::ReplaceCommandInCrontabJobs(tail, command, args, minutes, replaced)

    let AddToCrontabToExecEveryNMinutes (username: string, command: string, args: string, minutes: int, server: Server) =

        (* for when I have energy to accept all kinds of time inputs and outputs:

        FIXME: should use single TimeSpan param instead? (this way we don't need to deal with correcting inputs, e.g. 25:61h on the 31 of November)
        if (minute.IsSome && (minute.Value < 0 || minute.Value > 59)) then
             raise (new ArgumentException("minute should be between 0-59", "minute"))
        if (hour.IsSome && (hour.Value < 0 || hour.Value > 23)) then
             raise (new ArgumentException("hour should be between 0-23", "hour"))
        if (day.IsSome && (day.Value < 1 || day.Value > 31)) then
             raise (new ArgumentException("day should be between 1-31", "day"))
        if (month.IsSome && (month.Value < 1 || month.Value > 12)) then
             raise (new ArgumentException("month should be between 1-12", "month"))
        if (weekday.IsSome && (weekday.Value < 0 || weekday.Value > 6)) then
             raise (new ArgumentException("weekday should be between 0-6", "weekday"))


    //let AddToCrontabToExecDaily (commandWithArgs: string) =
    //    SafeHiddenExecBashCommand(String.Format("(crontab -l 2>/dev/null; echo \"@daily {0}\") | crontab -", commandWithArgs))

    //let AddToCrontabToExecHourly (commandWithArgs: string) =
    //    SafeHiddenExecBashCommand(String.Format("(crontab -l 2>/dev/null; echo \"@hourly {0}\") | crontab -", commandWithArgs))

        *)

        let needsSudo =
            if (server = ThisServer) then
                NeedsSudo(username)
            else
                true

        let listCommand =
            if (needsSudo) then
                { Command = "sudo"; Arguments = String.Format("crontab -u {0} -l", username) }
            else
                { Command = "crontab"; Arguments = "-l" }

        let currentUser = WhoAmI()

        let finalListCommand =
            match server with
            | ThisServer -> listCommand
            | RemoteServer(address) ->
                { Command = "ssh"; Arguments = String.Format("{0}@{1} \"{2}\"", currentUser, address, listCommand) }

        let procResult = Process.Execute(finalListCommand, Echo.Off)
        let curatedOutput =
            if not (procResult.ExitCode = 0) then
                String.Empty
            else
                procResult.Output.StdOut.Trim()

        let cronJobs = List.ofArray(curatedOutput.Split([| Environment.NewLine |], StringSplitOptions.RemoveEmptyEntries))

        let replacedCronJobs = Array.ofList(ReplaceCommandInCrontabJobs(cronJobs, command, args, minutes, false))

        let tempFile = Path.GetTempFileName()
        try
            File.WriteAllLines(tempFile, replacedCronJobs)
            ChangeMode(new FileInfo(tempFile), "777", false)

            let command =
                if (needsSudo) then
                    String.Format("cat {0} | sudo crontab -u {1} -", tempFile, username)
                else
                    String.Format("cat {0} | crontab -", tempFile)

            match server with
            | ThisServer ->
                SafeExecuteBashCommand(command, Echo.Off) |> ignore
            | RemoteServer(address) ->
                let shellWasTweaked =
                    if not (ShellIsEnabledForUserOnThisServer(username)) then
                        EnableShellForUser(username, Server.ThisServer)
                        true
                    else
                        false


                try
                    let cmdArgs = String.Format("runuser -l {0} -c 'scp {1} {0}@{2}:{3}'",
                                                username,
                                                tempFile,
                                                address,
                                                tempFile)
                    Process.SafeExecute({ Command = "sudo"; Arguments = cmdArgs }, Echo.All) |> ignore
                finally
                    if (shellWasTweaked) then
                        DisableShellForUser(username, Server.ThisServer)


                let sshArgs = String.Format("-t {0}@{1} \"{2} && sudo rm {3}\"",
                                            currentUser,
                                            address,
                                            command,
                                            tempFile)
                Process.SafeExecute({ Command = "ssh"; Arguments = sshArgs }, Echo.All) |> ignore

        finally
            File.Delete(tempFile)

    let ClearCrontab (username: string) =
        let cmd =
            if (NeedsSudo (username)) then
                { Command = "sudo"; Arguments = String.Format("crontab -u {0} -r", username) }
            else
                { Command = "crontab"; Arguments = "-r" }
        Process.Execute(cmd, Echo.Off) |> ignore
