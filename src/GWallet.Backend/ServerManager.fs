namespace GWallet.Backend

open System
open System.IO
open System.Linq

module ServerManager =

    let UpdateServersFile () =
        Console.WriteLine "INPUT:"
        let baseLineServers = Config.ExtractEmbeddedResourceFileContents ServerRegistry.ServersEmbeddedResourceFileName
                              |> ServerRegistry.Deserialize

        let fromElectrumServerToGenericServerDetails (es: UtxoCoin.ElectrumServer) =
            match es.UnencryptedPort with
            | None -> failwith "filtering for non-ssl electrum servers didn't work?"
            | Some unencryptedPort ->
                {
                    NetworkPath = es.Fqdn
                    ConnectionType = { Encrypted = false; Protocol = Tcp unencryptedPort }
                    CommunicationHistory = None
                }

        let btc = Currency.BTC
        let electrumBtcServers = UtxoCoin.ElectrumServerSeedList.ExtractServerListFromElectrumRepository btc
        let eyeBtcServers = UtxoCoin.ElectrumServerSeedList.ExtractServerListFromWebPage btc

        let baseLineBtcServers =
            match baseLineServers.TryGetValue btc with
            | true,baseLineBtcServers ->
                baseLineBtcServers
            | false,_ ->
                failwithf "There should be some %A servers as baseline" btc

        let allBtcServers = Seq.append electrumBtcServers eyeBtcServers
                            |> Seq.map fromElectrumServerToGenericServerDetails
                            |> Seq.append baseLineBtcServers

        let ltc = Currency.LTC
        let electrumLtcServers = UtxoCoin.ElectrumServerSeedList.ExtractServerListFromElectrumRepository ltc
        let eyeLtcServers = UtxoCoin.ElectrumServerSeedList.ExtractServerListFromWebPage ltc

        let baseLineLtcServers =
            match baseLineServers.TryGetValue ltc with
            | true,baseLineLtcServers ->
                baseLineLtcServers
            | false,_ ->
                failwithf "There should be some %A servers as baseline" ltc

        let allLtcServers = Seq.append electrumLtcServers eyeLtcServers
                            |> Seq.map fromElectrumServerToGenericServerDetails
                            |> Seq.append baseLineLtcServers

        for currency,servers in baseLineServers |> Map.toSeq do
            Console.WriteLine (sprintf "%i %A servers from baseline JSON file" (servers.Count()) currency)

            match currency with
            | Currency.BTC ->
                Console.WriteLine (sprintf "%i BTC servers from electrum repository" (electrumBtcServers.Count()))
                Console.WriteLine (sprintf "%i BTC servers from bitcoin-eye" (eyeBtcServers.Count()))
            | Currency.LTC ->
                Console.WriteLine (sprintf "%i LTC servers from electrum repository" (electrumLtcServers.Count()))
                Console.WriteLine (sprintf "%i LTC servers from bitcoin-eye" (eyeLtcServers.Count()))
            | _ ->
                ()

        let allCurrenciesServers =
            baseLineServers.Add(Currency.BTC, allBtcServers)
                           .Add(Currency.LTC, allLtcServers)

        let allServersJson = ServerRegistry.Serialize allCurrenciesServers
        File.WriteAllText(ServerRegistry.ServersEmbeddedResourceFileName, allServersJson)

        Console.WriteLine "OUTPUT:"
        let filteredOutServers = ServerRegistry.Deserialize allServersJson
        for currency,servers in filteredOutServers |> Map.toSeq do
            Console.WriteLine (sprintf "%i %A servers total" (servers.Count()) currency)
