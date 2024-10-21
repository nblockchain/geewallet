namespace GWallet.Backend

open System
open System.Net.Http

open Fsdk.FSharpUtil

open GWallet.Backend
open GWallet.Backend.UtxoCoin
open GWallet.Backend.FSharpUtil.UwpHacks


/// Abstract base class for clients for REST APIs such as Bitcore or Blockbook
[<AbstractClass>]
type RestAPIClient(serverAddress: string, ?maxConcurrentRequests: uint32) =
    let httpClient = new HttpClient(BaseAddress=Uri serverAddress, Timeout=Config.DEFAULT_NETWORK_TIMEOUTS.Timeout)

    let mutable lastRequestTime = DateTime.Now
    let minTimeBetweenRequests = 0.1
    let semaphore = new System.Threading.SemaphoreSlim(defaultArg maxConcurrentRequests 1u |> int)

    interface IDisposable with
        override self.Dispose (): unit = 
            httpClient.Dispose()
            semaphore.Dispose()

    member internal self.Request(request: string): Async<string> =
        async {
            try
                try
                    do! semaphore.WaitAsync() |> Async.AwaitTask
                    let diff = (DateTime.Now - lastRequestTime).TotalSeconds
                    if diff < minTimeBetweenRequests then
                        do! Async.Sleep <| int ((minTimeBetweenRequests - diff) * 1000.0)
                    let! result = httpClient.GetStringAsync request |> Async.AwaitTask
                    lastRequestTime <- DateTime.Now
                    return result
                finally
                    semaphore.Release() |> ignore
            with
            | ex ->
                match FindException<HttpRequestException> ex with
                | Some httpRequestExn ->
                    // maybe only discard server on several specific errors?
                    let msg = SPrintF2 "%s: %s" (httpRequestExn.GetType().FullName) httpRequestExn.Message
                    return raise <| ServerDiscardedException(msg, httpRequestExn)
                | _ -> ()
                match FindException<Threading.Tasks.TaskCanceledException> ex with
                | Some taskCancelledExn ->
                    let msg = SPrintF1 "Timeout: %s" taskCancelledExn.Message
                    return raise <| ServerDiscardedException(msg, taskCancelledExn)
                | _ -> ()
                return raise (ReRaise ex)
        }
