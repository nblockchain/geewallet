
namespace FSX.Infrastructure

open System
open System.Linq

open Process

module Git =

    let private gitCommand = "git"

    let rec private GetBranchFromGitBranch(outchunks: list<string>) =
        match outchunks with
        | [] -> failwith "current branch not found, unexpected output from `git branch`"
        | head::tail ->
            if (head.StartsWith("*")) then
                let branchName = head.Substring("* ".Length)
                branchName
            else
                GetBranchFromGitBranch(tail)

    let private CheckGitIsInstalled(): unit =
        if not (Process.CommandWorksInShell gitCommand) then
            Console.Error.WriteLine "Could not continue, install 'git' first"
            Environment.Exit 1

    let GetCurrentBranch() =
        CheckGitIsInstalled()
        let gitBranch = Process.Execute({ Command = gitCommand; Arguments = "branch" }, Echo.Off)
        if (gitBranch.ExitCode <> 0) then
            failwith "Unexpected git behaviour, `git branch` didn't succeed"

        let branchesOutput = gitBranch.Output.StdOut.Split([|Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries) |> List.ofSeq
        GetBranchFromGitBranch(branchesOutput)

    let GetLastCommit() =
        CheckGitIsInstalled()
        let gitLogCmd = { Command = gitCommand; Arguments = "log --no-color --first-parent -n1 --pretty=format:%h" }
        let gitLastCommit = Process.Execute(gitLogCmd, Echo.Off)
        if (gitLastCommit.ExitCode <> 0) then
            failwith "Unexpected git behaviour, as `git log` succeeded before but not now"

        let lines = gitLastCommit.Output.StdOut.Split([|Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries)
        if (lines.Length <> 1) then
            failwith "Unexpected git output for special git log command"
        lines.[0]

    let private random = Random()
    let private GenerateRandomShortNameWithLettersButNoNumbers(): string =
        let chars = "abcdefghijklmnopqrstuvwxyz"
        let randomCharArray = Enumerable.Repeat(chars, 8).Select(fun str -> str.[random.Next(str.Length)]).ToArray()
        String(randomCharArray)

    let private AddRemote (remoteName: string) (remoteUrl: string) =
        let gitRemoteAdd = { Command = gitCommand; Arguments = sprintf "remote add %s %s" remoteName remoteUrl }
        Process.SafeExecute(gitRemoteAdd, Echo.Off) |> ignore

    let private RemoveRemote (remoteName: string) =
        let gitRemoteRemove = { Command = gitCommand; Arguments = sprintf "remote remove %s" remoteName }
        Process.SafeExecute(gitRemoteRemove, Echo.Off) |> ignore

    let private FetchAll() =
        let gitFetchAll = { Command = gitCommand; Arguments = "fetch --all" }
        Process.SafeExecute(gitFetchAll, Echo.Off) |> ignore

    let private GetNumberOfCommitsBehindAndAheadFromRemoteBranch(repoUrl: string) (branchName: string): int*int =
        CheckGitIsInstalled()

        let lastCommit = GetLastCommit()

        let gitShowRemotes = { Command = gitCommand; Arguments = "remote -v" }
        let remoteLines = Process.SafeExecute(gitShowRemotes, Echo.Off)
                                      .Output.StdOut.Split([|Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries)
        let remoteFound = remoteLines.FirstOrDefault(fun line -> line.Contains("\t" + repoUrl + " "))
        let remote,cleanRemoteLater =
            if (remoteFound <> null) then
                remoteFound.Substring(0, remoteFound.IndexOf("\t")),false
            else
                let randomNameForRemoteToBeDeletedLater = GenerateRandomShortNameWithLettersButNoNumbers()
                AddRemote randomNameForRemoteToBeDeletedLater repoUrl
                FetchAll()
                randomNameForRemoteToBeDeletedLater,true

        let gitRevListCmd = { Command = gitCommand; Arguments = sprintf "rev-list --left-right --count %s/%s...%s" remote branchName lastCommit }
        let gitCommitDivergence = Process.SafeExecute(gitRevListCmd, Echo.Off)

        let numbers = gitCommitDivergence.Output.StdOut.Split([|"\t"|], StringSplitOptions.RemoveEmptyEntries)
        let expectedNumberOfNumbers = 2
        if (numbers.Length <> expectedNumberOfNumbers) then
            failwith (sprintf "Unexpected git output for special `git rev-list` command, got %d numbers instead of %d"
                          numbers.Length expectedNumberOfNumbers)
        let behind = Int32.Parse(numbers.[0])
        let ahead = Int32.Parse(numbers.[1])

        if (cleanRemoteLater) then
            RemoveRemote remote

        behind,ahead

    let GetNumberOfCommitsAhead repo branch: int =
        GetNumberOfCommitsBehindAndAheadFromRemoteBranch repo branch |> snd

    let GetNumberOfCommitsBehind repo branch: int =
        GetNumberOfCommitsBehindAndAheadFromRemoteBranch repo branch |> fst

    // 0 == last commit, 1 == second to last, and so on...
    let GetCommitMessageOfLastCommitNumber(number: int): string =
        if (number < 0) then
            failwith "Expected number param to be non-negative"

        CheckGitIsInstalled()

        let gitLogCmd = { Command = gitCommand; Arguments = String.Format("log --skip={0} -1 --pretty=format:%b", number) }
        let gitLastNCommit = Process.SafeExecute(gitLogCmd, Echo.Off)
        gitLastNCommit.Output.StdOut


    let GetCommitMessagesOfCommitsInThisBranchNotPresentInRemoteBranch repo branch: seq<string>=
        seq {
            for i = 0 to (GetNumberOfCommitsAhead repo branch)-1 do
                yield GetCommitMessageOfLastCommitNumber i
        }

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
