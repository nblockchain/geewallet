namespace GWallet.Backend.Tests

open System

open NUnit.Framework

open GWallet.Backend
open GWallet.Backend.UtxoCoin

module ElectrumIntegrationTests =

    // probably a satoshi address because it was used in blockheight 2 and is unspent yet
    let SATOSHI_ADDRESS =
        // funny that it almost begins with "1HoDL"
        "1HLoD9E4SDFFPDiYfNYnkBLQ85Y51J3Zb1"

    // https://medium.com/@SatoshiLite/satoshilite-1e2dad89a017
    let LTC_GENESIS_BLOCK_ADDRESS = "Ler4HNAEfwYhBmGXcFP2Po1NpRUEiK8km2"

    let CheckServerIsReachable (electrumServer: ElectrumServer)
                               (address: string)
                               (maybeFilter: Option<ElectrumServer -> bool>)
                               : Option<ElectrumServer> =
        let innerCheck server =
            try
                use electrumClient = new ElectrumClient(electrumServer)
                let balance = electrumClient.GetBalance address

                // if these ancient addresses get withdrawals it would be interesting in the crypto space...
                // so let's make the test check a balance like this which is unlikely to change
                Assert.That(balance.Confirmed, Is.Not.LessThan(998292))

                Some electrumServer
            with
            | :? JsonRpcSharp.ConnectionUnsuccessfulException as ex ->
                // to make sure this exception type is an abstract class
                Assert.That(ex.GetType(), Is.Not.EqualTo(typeof<JsonRpcSharp.ConnectionUnsuccessfulException>))
                None

        match maybeFilter with
        | Some filterFunc ->
            if (filterFunc electrumServer) then
                innerCheck electrumServer
            else
                None
        | _ ->
            innerCheck electrumServer

    (* some servers are already rejecting v0.10-protocol clients, with:
    System.AggregateException: One or more errors occurred. --->
    System.Exception: Error received from Electrum server electrum.hsmiths.com: 'unsupported protocol version: 0.10' (code '1').
      Original request sent from client: '{"id":0,"method":"server.version","params":["2.8.3","0.10"]}'
      at <StartupCode$GWallet-Backend>.$FaultTolerantParallelClient+asyncJobsToRunInParallelAsAsync@134-3[E,R].Invoke (System.Exception _arg1) [0x000a9] in <5acc87745952d4f0a74503837487cc5a>:0 
      at Microsoft.FSharp.Control.AsyncBuilderImpl+tryWithExnA@881[a].Invoke (System.Runtime.ExceptionServices.ExceptionDispatchInfo edi) [0x0000d] in <59964427904cf4daa745038327449659>:0 
      at Microsoft.FSharp.Control.AsyncBuilderImpl+callA@839[b,a].Invoke (Microsoft.FSharp.Control.AsyncParams`1[T] args) [0x00052] in <59964427904cf4daa745038327449659>:0 
       --- End of inner exception stack trace ---
      at System.Threading.Tasks.Task.ThrowIfExceptional (System.Boolean includeTaskCanceledExceptions) [0x00011] in <e22c1963d07746cd9708456620d50e1a>:0 
      at System.Threading.Tasks.Task`1[TResult].GetResultCore (System.Boolean waitCompletionNotification) [0x0002b] in <e22c1963d07746cd9708456620d50e1a>:0 
      at System.Threading.Tasks.Task`1[TResult].get_Result () [0x0000f] in <e22c1963d07746cd9708456620d50e1a>:0 
      at GWallet.Backend.FaultTolerantParallelClient`1[E].WhenSomeInternal[T,R] (System.Int32 numberOfResultsRequired, Microsoft.FSharp.Collections.FSharpList`1[T] tasks, Microsoft.FSharp.Collections.FSharpList`1[T] resultsSoFar, Microsoft.FSharp.Collections.FSharpList`1[T] failedFuncsSoFar) [0x0009a] in <5acc87745952d4f0a74503837487cc5a>:0 
      at GWallet.Backend.FaultTolerantParallelClient`1[E].WhenSome[T,R] (System.Int32 numberOfConsistentResultsRequired, System.Collections.Generic.IEnumerable`1[T] jobs, Microsoft.FSharp.Collections.FSharpList`1[T] resultsSoFar, Microsoft.FSharp.Collections.FSharpList`1[T] failedFuncsSoFar) [0x00012] in <5acc87745952d4f0a74503837487cc5a>:0 
      at GWallet.Backend.FaultTolerantParallelClient`1[E].QueryInternal[T,R] (T args, Microsoft.FSharp.Collections.FSharpList`1[T] funcs, Microsoft.FSharp.Collections.FSharpList`1[T] resultsSoFar, Microsoft.FSharp.Collections.FSharpList`1[T] failedFuncsSoFar, System.UInt16 retries) [0x001cf] in <5acc87745952d4f0a74503837487cc5a>:0 
      at <StartupCode$GWallet-Backend>.$FaultTolerantParallelClient+Query@165[R,E,T].Invoke (Microsoft.FSharp.Core.Unit unitVar) [0x00023] in <5acc87745952d4f0a74503837487cc5a>:0 
      at Microsoft.FSharp.Control.AsyncBuilderImpl+callA@839[b,a].Invoke (Microsoft.FSharp.Control.AsyncParams`1[T] args) [0x00052] in <59964427904cf4daa745038327449659>:0 
    --- End of stack trace from previous location where exception was thrown ---
      at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw () [0x0000c] in <e22c1963d07746cd9708456620d50e1a>:0 
      at Microsoft.FSharp.Control.AsyncBuilderImpl.commit[a] (Microsoft.FSharp.Control.AsyncBuilderImpl+AsyncImplResult`1[T] res) [0x0002d] in <59964427904cf4daa745038327449659>:0 
      at Microsoft.FSharp.Control.CancellationTokenOps.RunSynchronouslyInCurrentThread[a] (System.Threading.CancellationToken token, Microsoft.FSharp.Control.FSharpAsync`1[T] computation) [0x00029] in <59964427904cf4daa745038327449659>:0 
      at Microsoft.FSharp.Control.CancellationTokenOps.RunSynchronously[a] (System.Threading.CancellationToken token, Microsoft.FSharp.Control.FSharpAsync`1[T] computation, Microsoft.FSharp.Core.FSharpOption`1[T] timeout) [0x00014] in <59964427904cf4daa745038327449659>:0 
      at Microsoft.FSharp.Control.FSharpAsync.RunSynchronously[T] (Microsoft.FSharp.Control.FSharpAsync`1[T] computation, Microsoft.FSharp.Core.FSharpOption`1[T] timeout, Microsoft.FSharp.Core.FSharpOption`1[T] cancellationToken) [0x00071] in <59964427904cf4daa745038327449659>:0 
      at GWallet.Frontend.Console.UserInteraction.DisplayAccountStatuses (GWallet.Frontend.Console.WhichAccount whichAccount) [0x00057] in <5acc877848ab50f3a74503837887cc5a>:0 
      at Program.ProgramMainLoop[a] () [0x0000d] in <5acc877848ab50f3a74503837887cc5a>:0 
      at Program.main (System.String[] argv) [0x00008] in <5acc877848ab50f3a74503837887cc5a>:0 
    ---> (Inner Exception #0) System.Exception: Error received from Electrum server electrum.hsmiths.com: 'unsupported protocol version: 0.10' (code '1'). Original request sent from client: '{"id":0,"method":"server.version","params":["2.8.3","0.10"]}'
      at <StartupCode$GWallet-Backend>.$FaultTolerantParallelClient+asyncJobsToRunInParallelAsAsync@134-3[E,R].Invoke (System.Exception _arg1) [0x000a9] in <5acc87745952d4f0a74503837487cc5a>:0 
      at Microsoft.FSharp.Control.AsyncBuilderImpl+tryWithExnA@881[a].Invoke (System.Runtime.ExceptionServices.ExceptionDispatchInfo edi) [0x0000d] in <59964427904cf4daa745038327449659>:0 
      at Microsoft.FSharp.Control.AsyncBuilderImpl+callA@839[b,a].Invoke (Microsoft.FSharp.Control.AsyncParams`1[T] args) [0x00052] in <59964427904cf4daa745038327449659>:0 <---
    *)
    let FilterUnfriendlyServers electrumServer =
        if (electrumServer.Fqdn.EndsWith "hsmiths.com" ||
            electrumServer.Fqdn = "btc.cihar.com" ||
            electrumServer.Fqdn = "electrum.leblancnet.us" ||
            electrumServer.Fqdn = "electrum.qtornado.com" ||
            electrumServer.Fqdn = "ndnd.selfhost.eu") then
            false
        else
            true

    [<Test>]
    let ``can retreive electrum BTC servers``() =
        let reachableServers = seq {
            for electrumServer in ElectrumServerSeedList.DefaultBtcList do
                match CheckServerIsReachable electrumServer SATOSHI_ADDRESS (Some FilterUnfriendlyServers) with
                | Some server ->
                    Console.WriteLine (sprintf "BTC server %s is reachable" server.Fqdn)
                    yield server
                | None ->
                    Console.WriteLine (sprintf "BTC server %s is unreachable or discarded" electrumServer.Fqdn)
                    ()
        }
        let reachableServersCount = (reachableServers |> List.ofSeq).Length
        Console.WriteLine (sprintf "%d BTC servers were reachable" reachableServersCount)
        Assert.That(reachableServersCount, Is.GreaterThan(1))

    [<Test>]
    let ``can retreive electrum LTC servers``() =
        let reachableServers = seq {
            for electrumServer in ElectrumServerSeedList.DefaultLtcList do
                match CheckServerIsReachable electrumServer LTC_GENESIS_BLOCK_ADDRESS None with
                | Some server ->
                    Console.WriteLine (sprintf "LTC server %s is reachable" server.Fqdn)
                    yield server
                | None ->
                    Console.WriteLine (sprintf "LTC server %s is unreachable" electrumServer.Fqdn)
                    ()
        }
        let reachableServersCount = (reachableServers |> List.ofSeq).Length
        Console.WriteLine (sprintf "%d LTC servers were reachable" reachableServersCount)
        Assert.That(reachableServersCount, Is.GreaterThan(1))
