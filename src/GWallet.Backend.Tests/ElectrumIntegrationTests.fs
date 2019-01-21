namespace GWallet.Backend.Tests

open System
open System.Linq

open NUnit.Framework

open NBitcoin

open GWallet.Backend
open GWallet.Backend.UtxoCoin

// TODO: move this to its own file
[<TestFixture>]
type ElectrumServerUnitTests() =

    [<Test>]
    member __.``filters electrum BTC servers``() =
        for electrumServer in ElectrumServerSeedList.DefaultBtcList do
            Assert.That (electrumServer.UnencryptedPort, Is.Not.EqualTo(None),
                sprintf "BTC servers list should be filtered against only-TLS compatible servers, but %s was found"
                        electrumServer.Fqdn)

            Assert.That (electrumServer.Fqdn, Is.Not.StringEnding(".onion"),
                sprintf "BTC servers list should be filtered against onion servers, but %s was found"
                        electrumServer.Fqdn)

    [<Test>]
    member __.``filters electrum LTC servers``() =
        for electrumServer in ElectrumServerSeedList.DefaultLtcList do
            Assert.That (electrumServer.UnencryptedPort, Is.Not.EqualTo(None),
                sprintf "BTC servers list should be filtered against only-TLS compatible servers, but %s was found"
                        electrumServer.Fqdn)

            Assert.That (electrumServer.Fqdn, Is.Not.StringEnding(".onion"),
                sprintf "BTC servers list should be filtered against onion servers, but %s was found"
                        electrumServer.Fqdn)

[<TestFixture>]
type ElectrumIntegrationTests() =

    // probably a satoshi address because it was used in blockheight 2 and is unspent yet
    let SCRIPTHASH_OF_SATOSHI_ADDRESS =
        // funny that it almost begins with "1HoDL"
        UtxoCoin.Account.GetElectrumScriptHashFromPublicAddress Currency.BTC "1HLoD9E4SDFFPDiYfNYnkBLQ85Y51J3Zb1"


    // https://medium.com/@SatoshiLite/satoshilite-1e2dad89a017
    let SCRIPTHASH_OF_LTC_GENESIS_BLOCK_ADDRESS =
        UtxoCoin.Account.GetElectrumScriptHashFromPublicAddress Currency.LTC "Ler4HNAEfwYhBmGXcFP2Po1NpRUEiK8km2"

    let CheckServerIsReachable (electrumServer: ElectrumServer)
                               (currency: Currency)
                               (maybeFilter: Option<ElectrumServer -> bool>)
                               : Async<Option<ElectrumServer>> = async {

        let scriptHash =
            match currency with
            | Currency.BTC -> SCRIPTHASH_OF_SATOSHI_ADDRESS
            | Currency.LTC -> SCRIPTHASH_OF_LTC_GENESIS_BLOCK_ADDRESS
            | _ -> failwith "Tests not ready for this currency"

        let innerCheck server =
            // this try-with block is similar to the one in UtxoCoinAccount, where it rethrows as
            // ElectrumServerDiscarded error, but here we catch 2 of the 3 errors that are caught there
            // because we want the server incompatibilities to show up here (even if GWallet clients bypass
            // them in order not to crash)
            try
                let balance = ElectrumClient.GetBalance electrumServer scriptHash
                                  |> Async.RunSynchronously

                // if these ancient addresses get withdrawals it would be interesting in the crypto space...
                // so let's make the test check a balance like this which is unlikely to change
                Assert.That(balance.Confirmed, Is.Not.LessThan(998292))

                Console.WriteLine (sprintf "%A server %s is reachable" currency server.Fqdn)
                Some electrumServer
            with
            | :? ConnectionUnsuccessfulException as ex ->
                // to make sure this exception type is an abstract class
                Assert.That(ex.GetType(), Is.Not.EqualTo(typeof<ConnectionUnsuccessfulException>))

                let exDescription = sprintf "%s: %s" (ex.GetType().Name) ex.Message

                Console.Error.WriteLine (sprintf "%s -> %A server %s is unreachable" exDescription currency server.Fqdn)
                None
            | :? ElectrumServerReturningInternalErrorException as ex ->
                Console.Error.WriteLine (sprintf "%A server %s is unhealthy" currency server.Fqdn)
                None

        match maybeFilter with
        | Some filterFunc ->
            if (filterFunc electrumServer) then
                return innerCheck electrumServer
            else
                return None
        | _ ->
            return innerCheck electrumServer

        }

    let rec AtLeastNJobsWork(jobs: List<Async<Option<ElectrumServer>>>) (minimumCountNeeded: uint16): unit =
        match jobs with
        | [] ->
            Assert.Fail ("Not enough servers were reached")
        | head::tail ->
            match head |> Async.RunSynchronously with
            | None ->
                AtLeastNJobsWork tail minimumCountNeeded
            | Some _ ->
                let newCount = (minimumCountNeeded-(uint16 1))
                if newCount <> (uint16 0) then
                    AtLeastNJobsWork tail newCount

    let CheckElectrumServersConnection electrumServers currency =
        let reachServerJobs = seq {
            for electrumServer in electrumServers do
                yield CheckServerIsReachable electrumServer currency None
        }
        AtLeastNJobsWork (reachServerJobs |> List.ofSeq)
                         // more than one
                         (uint16 2)

    [<Test>]
(*
    [<Ignore "see https://gitlab.com/DiginexGlobal/geewallet/issues/49">]
 *)
    member __.``can connect to some electrum BTC servers``() =
        CheckElectrumServersConnection ElectrumServerSeedList.DefaultBtcList Currency.BTC

    [<Test>]
(*
    [<Ignore "see https://gitlab.com/DiginexGlobal/geewallet/issues/49">]
 *)
    member __.``can connect to some electrum LTC servers``() =
        CheckElectrumServersConnection ElectrumServerSeedList.DefaultLtcList Currency.LTC
