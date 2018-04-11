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

                // TODO: make sure the type is not ServerTooNewException, to catch protocol version mismatches earlier
                None

        match maybeFilter with
        | Some filterFunc ->
            if (filterFunc electrumServer) then
                innerCheck electrumServer
            else
                None
        | _ ->
            innerCheck electrumServer

    [<Test>]
    let ``can retreive electrum BTC servers``() =
        let reachableServers = seq {
            for electrumServer in ElectrumServerSeedList.DefaultBtcList do
                match CheckServerIsReachable electrumServer SATOSHI_ADDRESS None with
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
