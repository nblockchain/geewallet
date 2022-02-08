namespace GWallet.Backend.UtxoCoin

open System
open System.Net
open System.Text.RegularExpressions

open NOnion
open NOnion.Directory
open NOnion.Services

open GWallet.Backend


module internal TorOperations =
    let GetRandomTorFallbackDirectoryEndPoint() =
        match Caching.Instance.GetServers
            (ServerType.ProtocolServer ServerProtocol.Tor)
            |> Shuffler.Unsort
            |> Seq.tryHead with
        | Some server ->
            match server.ServerInfo.ConnectionType.Protocol with
            | Protocol.Tcp port ->
                IPEndPoint(IPAddress.Parse server.ServerInfo.NetworkPath, int32 port)
            | _ -> failwith "Invalid Tor directory. Tor directories must have an IP and port."
        | None ->
            failwith "Couldn't find any Tor server"

    let internal GetTorDirectory(): Async<TorDirectory> =
        async {
            return! FSharpUtil.Retry<TorDirectory, NOnionException>
                (fun _ -> TorDirectory.Bootstrap (GetRandomTorFallbackDirectoryEndPoint()))
                Config.TOR_CONNECTION_RETRY_COUNT
        }

    let internal StartTorServiceHost directory =
        async {
            return! FSharpUtil.Retry<TorServiceHost, NOnionException>
                (fun _ -> async { 
                    let torHost = TorServiceHost(directory, Config.TOR_CONNECTION_RETRY_COUNT) 
                    do! torHost.Start()
                    return torHost
                })
                Config.TOR_CONNECTION_RETRY_COUNT
        }

    let internal TorConnect directory introductionPoint =
        async {
            return! FSharpUtil.Retry<TorServiceClient, NOnionException>
                (fun _ -> TorServiceClient.Connect directory introductionPoint)
                Config.TOR_CONNECTION_RETRY_COUNT
        }

    let internal ExtractServerListFromGithub() : List<(string*string)> =
        let urlToTorServerList = "https://raw.githubusercontent.com/torproject/tor/main/src/app/config/fallback_dirs.inc"
        use webClient = new WebClient()
        let fetchedInfo: string = webClient.DownloadString urlToTorServerList

        let ipv4Pattern: string = "\"([0-9\.]+)\sorport=(\S*)\sid=(\S*)\""
        let matches = Regex.Matches(fetchedInfo, ipv4Pattern)

        matches
        |> Seq.cast
        |> Seq.map (fun (regMatch: Match) ->
            (regMatch.Groups.[1].Value, regMatch.Groups.[2].Value))
        |> Seq.toList
