namespace GWallet.Backend.UtxoCoin

open System
open System.IO
open System.Net
open System.Text.RegularExpressions

open NOnion
open NOnion.Directory
open NOnion.Services
open Org.BouncyCastle.Crypto.Parameters
open Org.BouncyCastle.Crypto.Generators
open Org.BouncyCastle.Security

open GWallet.Backend


module internal TorOperations =
    let private GetTorServiceKey () =
        let serviceKeyPath =
            Path.Combine (Config.GetConfigDirForThisProgram().FullName, "torServiceKey.bin")

        if File.Exists serviceKeyPath then
            let serviceKeyBytes =
                File.ReadAllBytes serviceKeyPath
            Ed25519PrivateKeyParameters(serviceKeyBytes, 0)
        else
            let random = SecureRandom()
            let kpGen = Ed25519KeyPairGenerator()
            kpGen.Init(Ed25519KeyGenerationParameters random)
            let masterPrivateKey = kpGen.GenerateKeyPair().Private :?> Ed25519PrivateKeyParameters
            File.WriteAllBytes(serviceKeyPath, masterPrivateKey.GetEncoded())
            masterPrivateKey

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
                (fun _ -> TorDirectory.Bootstrap (GetRandomTorFallbackDirectoryEndPoint()) (Config.GetCacheDir()))
                Config.TOR_CONNECTION_RETRY_COUNT
        }

    let internal StartTorServiceHost directory =
        async {
            return! FSharpUtil.Retry<TorServiceHost, NOnionException>
                (fun _ -> async { 
                    let torHost = TorServiceHost(directory, Config.TOR_DESCRIPTOR_UPLOAD_RETRY_COUNT, Config.TOR_CONNECTION_RETRY_COUNT, GetTorServiceKey() |> Some) 
                    do! torHost.Start()
                    return torHost
                })
                Config.TOR_CONNECTION_RETRY_COUNT
        }

    let internal TorConnect directory url =
        async {
            return! FSharpUtil.Retry<TorServiceClient, NOnionException>
                (fun _ -> TorServiceClient.Connect directory url)
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
