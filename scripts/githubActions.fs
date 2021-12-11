namespace GWallet.Github

open System
open System.IO
open System.Linq
open System.Threading
open System.Text
open System.Configuration
open System.Net.Http
open System.Net.Http.Headers
open System.Web.Script.Serialization
open System.Collections
open System.Collections.Generic

module GithubActions =
    let private SendRequest (url: string) =
        async {
            use client = new HttpClient()
            client.DefaultRequestHeaders.Accept.Add(MediaTypeWithQualityHeaderValue.Parse "application/vnd.github.v3+json")
            client.DefaultRequestHeaders.UserAgent.Add(ProductInfoHeaderValue("CIChecker", "1.0.0"))
            return! client.GetStringAsync url |> Async.AwaitTask
        }

    let private QueryRunCount
        (orgOrUsername: string)
        (repoName: string)
        (status: string)
        (lastCommit: string)
        (currentBranch:string)
        =
        async {
            let url =
                sprintf
                    "https://api.github.com/repos/%s/%s/actions/runs?status=%s&branch=%s"
                    orgOrUsername
                    repoName
                    status
                    currentBranch

            Console.WriteLine (sprintf "Querying github API: %s" url)

            let! response = SendRequest url

            let responseObj =
                JavaScriptSerializer().Deserialize<Dictionary<string, obj>> response

            match responseObj.TryGetValue "workflow_runs" with
            | true, workflowRuns ->
                match workflowRuns with
                | :? ArrayList as runsArray ->
                    return
                        runsArray.OfType<Dictionary<string, obj>>()
                        |> Seq.filter
                            (fun run ->
                                match run.TryGetValue "head_sha" with
                                | false, _ ->
                                    failwithf "Couldn't find 'head_sha' in sub-JSON: %s" response
                                | true, headSha ->
                                    match headSha with
                                    | :? string as headShaString ->
                                        lastCommit.StartsWith headShaString || headShaString.StartsWith lastCommit
                                    | _ ->
                                        failwithf "Couldn't cast 'head_sha' to string: %s" response
                            )
                        |> Seq.length
                | _ -> return failwithf "Couldn't cast 'workflow_runs' to ArrayList: %s" response
            | false, _ ->
                return failwithf "Couldn't find 'workflow_runs' in JSON: %s" response

        }

    let private CheckAllRuns
        (orgOrUsername: string)
        (repoName: string)
        (lastCommit: string)
        (currentBranch: string)
        =
        async {
            let! successfulCount = QueryRunCount orgOrUsername repoName "success" lastCommit currentBranch
            let! failedCount = QueryRunCount orgOrUsername repoName "failure" lastCommit currentBranch

            if failedCount > 0 || successfulCount < 1 then
                return false
            else
                return true
        }

    let MakeSureGithubCIPassed
        (orgOrUsername: string)
        (repoName: string)
        (lastCommit: string)
        (currentBranch: string)
        =
        if CheckAllRuns orgOrUsername repoName lastCommit currentBranch
           |> Async.RunSynchronously then
            Console.WriteLine (sprintf "GitHubCI is green for branch %s (commit %s)" currentBranch lastCommit)
        else
            failwithf "Failed job in GitHub: https://github.com/%s/%s/commit/%s"
                orgOrUsername
                repoName
                lastCommit
