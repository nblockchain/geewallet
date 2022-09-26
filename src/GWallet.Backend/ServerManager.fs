namespace GWallet.Backend

open System
open System.IO
open System.Linq

open GWallet.Backend.FSharpUtil.UwpHacks
open GWallet.Backend.UtxoCoin

module ServerManager =

    let UpdateServersFile () =
        Infrastructure.LogInfo "INPUT:"
        let baseLineServers = Config.ExtractEmbeddedResourceFileContents ServerRegistry.ServersEmbeddedResourceFileName
                              |> ServerRegistry.Deserialize

        let fromElectrumServerToGenericServerDetails (es: UtxoCoin.ElectrumServer) =
            match es.UnencryptedPort with
            | None -> failwith "filtering for non-ssl electrum servers didn't work?"
            | Some unencryptedPort ->
                {
                    ServerInfo =
                        {
                            NetworkPath = es.Fqdn
                            ConnectionType = { Encrypted = false; Protocol = Tcp unencryptedPort }
                        }
                    CommunicationHistory = None
                }

        let fromTorServerToGenericServerDetails (networkPath: string, port: string) =
            {
                ServerInfo =
                    {
                        NetworkPath = networkPath
                        ConnectionType = { Encrypted = false; Protocol = port |> uint32 |> Tcp }
                    }
                CommunicationHistory = None
            }

        let btc = Currency.BTC
        let electrumBtcServers = UtxoCoin.ElectrumServerSeedList.ExtractServerListFromElectrumRepository btc
        let eyeBtcServers = UtxoCoin.ElectrumServerSeedList.ExtractServerListFromWebPage btc

        let baseLineBtcServers =
            match baseLineServers.TryGetValue (ServerType.CurrencyServer btc) with
            | true,baseLineBtcServers ->
                baseLineBtcServers
            | false,_ ->
                failwith <| SPrintF1 "There should be some %A servers as baseline" btc

        let allBtcServers = Seq.append electrumBtcServers eyeBtcServers
                            |> Seq.map fromElectrumServerToGenericServerDetails
                            |> Seq.append baseLineBtcServers

        let ltc = Currency.LTC
        let electrumLtcServers = UtxoCoin.ElectrumServerSeedList.ExtractServerListFromElectrumRepository ltc
        let eyeLtcServers = UtxoCoin.ElectrumServerSeedList.ExtractServerListFromWebPage ltc

        let baseLineLtcServers =
            match baseLineServers.TryGetValue (ServerType.CurrencyServer ltc) with
            | true,baseLineLtcServers ->
                baseLineLtcServers
            | false,_ ->
                failwith <| SPrintF1 "There should be some %A servers as baseline" ltc

        let torServers = UtxoCoin.TorOperations.ExtractServerListFromGithub ()
        let allTorServers =
            torServers
            |> Seq.map fromTorServerToGenericServerDetails

        let allLtcServers = Seq.append electrumLtcServers eyeLtcServers
                            |> Seq.map fromElectrumServerToGenericServerDetails
                            |> Seq.append baseLineLtcServers

        for KeyValue(serverType, servers) in baseLineServers do
            Infrastructure.LogInfo (SPrintF2 "%i %A servers from baseline JSON file" (servers.Count()) serverType)

            match serverType with
            | ServerType.CurrencyServer Currency.BTC ->
                Infrastructure.LogInfo (SPrintF1 "%i BTC servers from electrum repository" (electrumBtcServers.Count()))
                Infrastructure.LogInfo (SPrintF1 "%i BTC servers from bitcoin-eye" (eyeBtcServers.Count()))
            | ServerType.CurrencyServer Currency.LTC ->
                Infrastructure.LogInfo (SPrintF1 "%i LTC servers from electrum repository" (electrumLtcServers.Count()))
                Infrastructure.LogInfo (SPrintF1 "%i LTC servers from bitcoin-eye" (eyeLtcServers.Count()))
            | _ ->
                ()

        let allCurrenciesServers =
            baseLineServers.Add(ServerType.CurrencyServer Currency.BTC, allBtcServers)
                           .Add(ServerType.CurrencyServer Currency.LTC, allLtcServers)
                           .Add(ServerType.ProtocolServer ServerProtocol.Tor, allTorServers)

        let allServersJson = ServerRegistry.Serialize allCurrenciesServers
        File.WriteAllText(ServerRegistry.ServersEmbeddedResourceFileName, allServersJson)

        Infrastructure.LogInfo "OUTPUT:"
        let filteredOutServers = ServerRegistry.Deserialize allServersJson
        for KeyValue(currency,servers) in filteredOutServers do
            Infrastructure.LogInfo (SPrintF2 "%i %A servers total" (servers.Count()) currency)

    let private tester =
        FaultTolerantParallelClient<ServerDetails,CommunicationUnsuccessfulException>
            Caching.Instance.SaveServerLastStat

    let private testingSettings =
        {
            NumberOfParallelJobsAllowed = 4u

            // if not zero we might screw up our percentage logging when performing the requests?
            NumberOfRetries = 0u
            NumberOfRetriesForInconsistency = 0u

            ResultSelectionMode = Exhaustive

            ExceptionHandler = None
        }

    let private GetDummyBalanceAction (currency: Currency) servers =

        let retrievalFuncs =
            if (currency.IsUtxo()) then
                let scriptHash =
                    match currency with
                    | Currency.BTC ->
                        // probably a satoshi address because it was used in blockheight 2 and is unspent yet
                        let SATOSHI_ADDRESS = "1HLoD9E4SDFFPDiYfNYnkBLQ85Y51J3Zb1"
                        // funny that it almost begins with "1HoDL"
                        UtxoCoin.Account.GetElectrumScriptHashFromPublicAddress currency SATOSHI_ADDRESS
                    | Currency.LTC ->
                        // https://medium.com/@SatoshiLite/satoshilite-1e2dad89a017
                        let LTC_GENESIS_BLOCK_ADDRESS = "Ler4HNAEfwYhBmGXcFP2Po1NpRUEiK8km2"
                        UtxoCoin.Account.GetElectrumScriptHashFromPublicAddress currency LTC_GENESIS_BLOCK_ADDRESS
                    | _ ->
                        failwith <| SPrintF1 "Currency %A not UTXO?" currency
                let utxoFunc electrumServer =
                    async {
                        let! bal = UtxoCoin.ElectrumClient.GetBalance scriptHash electrumServer
                        return bal.Confirmed |> decimal
                    }
                UtxoCoin.Server.GetServerFuncs utxoFunc servers |> Some

            elif currency.IsEther() then
                let ETH_GENESISBLOCK_ADDRESS = "0x0000000000000000000000000000000000000000"

                let web3Func (web3: Ether.SomeWeb3): Async<decimal> =
                    async {
                        let! balance = Async.AwaitTask (web3.Eth.GetBalance.SendRequestAsync ETH_GENESISBLOCK_ADDRESS)
                        return balance.Value |> decimal
                    }

                Ether.Server.GetServerFuncs web3Func servers currency
                    |> Some

            else
                None

        match retrievalFuncs with
        | Some queryFuncs ->
            async {
                try
                    let! _ = tester.Query testingSettings
                                          (queryFuncs |> List.ofSeq)
                    return ()
                with
                | :? NoneAvailableException ->
                    return ()
            } |> Some
        | _ ->
            None

    let private UpdateBaseline() =
        match Caching.Instance.ExportServers() with
        | None -> failwith "After updating servers, cache should not be empty"
        | Some serversInJson ->
            File.WriteAllText(ServerRegistry.ServersEmbeddedResourceFileName, serversInJson)

    let UpdateServersStats () =
        let jobs = seq {
            for currency in Currency.GetAll() do

                // because ETH tokens use ETH servers
                if not (currency.IsEthToken()) then
                    let serversForSpecificCurrency = Caching.Instance.GetServers (ServerType.CurrencyServer currency)
                    match GetDummyBalanceAction currency serversForSpecificCurrency with
                    | None -> ()
                    | Some job -> yield job
        }

        let torServers = Caching.Instance.GetServers (ServerType.ProtocolServer Tor)
        let torJobs = seq {
            for torServer in torServers do
                let job = 
                    async {
                        try
                            do TorOperations.GetTorGuardForServer torServer |> ignore
                        with
                        | :? NOnion.NOnionException -> ()
                    }
                yield job  
        }

        let allJobs = Seq.append jobs torJobs
        Async.Parallel allJobs
        |> Async.RunSynchronously
        |> ignore<array<unit>>

        UpdateBaseline()

