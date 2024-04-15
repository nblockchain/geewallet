namespace GWallet.Backend.UtxoCoin

open System

open ElectrumSharp

open GWallet.Backend

module Electrum =
    let CreateClientFor (electrumServer: ServerDetails): Async<ElectrumClient> =
        match electrumServer.ServerInfo.ConnectionType with
        | { Encrypted = true; Protocol = _ } -> failwith "Incompatibility filter for non-encryption didn't work?"
        | { Encrypted = false; Protocol = Http } -> failwith "HTTP server for UtxoCoin?"
        | { Encrypted = false; Protocol = Tcp port } ->
            async {
                let! client =
                    Electrum.CreateClient 
                        electrumServer.ServerInfo.NetworkPath 
                        port 
                        Config.DEFAULT_NETWORK_CONNECT_TIMEOUT 
                        "geewallet"
                client.Logger <- Infrastructure.LogDebug
                return client
            }

    let GetBalance (scriptHash: string) (electrumClient: Async<ElectrumClient>) =
        Electrum.GetBalances (List.singleton scriptHash) electrumClient
