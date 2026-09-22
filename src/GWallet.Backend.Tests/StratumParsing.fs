namespace GWallet.Backend.Tests

open System

open NUnit.Framework

open System.Net

open GWallet.Backend
open GWallet.Backend.UtxoCoin

[<TestFixture>]
type StratumParsing() =

    [<Test>]
    member __.``deserialize a nonJSON response fails with proper exception so that server can be ignored``() =
        let errorResponse = String.Empty

        let _ex = Assert.Throws<ServerMisconfiguredException>(fun _ ->
            StratumClient.Deserialize<BlockchainScriptHashGetBalanceResult> errorResponse |> ignore
        )

        let errorResponse = "this is not valid json"

        let ex = Assert.Throws<ServerMisconfiguredException>(fun _ ->
            StratumClient.Deserialize<BlockchainScriptHashGetBalanceResult> errorResponse |> ignore
        )
        Assert.That(ex.Message.Contains errorResponse)

    [<Test>]
    member __.``deserialize a successful balance response``() =
        let balanceResponse = "{\"id\": 1, \"result\": {\"confirmed\": 1, \"unconfirmed\": 2}}"

        let balance = StratumClient.Deserialize<BlockchainScriptHashGetBalanceResult> balanceResponse
        Assert.That(balance.Result.Confirmed, Is.EqualTo(1))
        Assert.That(balance.Result.Unconfirmed, Is.EqualTo(2))

    [<Test>]
    member __.``deserialize a stratum error from an electrum client doesn't fail``() =
        let errorResponse = "{\"jsonrpc\": \"2.0\", \"id\": 0, \"error\": {\"message\": \"internal error processing request\", \"code\": -32603}}"
        
        let ex = Assert.Throws<ElectrumServerReturningErrorInJsonResponseException>(fun _ ->
            StratumClient.Deserialize<BlockchainScriptHashGetBalanceResult> errorResponse
            |> ignore<BlockchainScriptHashGetBalanceResult>
        )
        Assert.That(ex.ErrorCode, Is.EqualTo(Some -32603))
    
    [<Test>]
    member __.``can deserialize error result with string error``() =
        let ex = Assert.Throws<ElectrumServerReturningErrorInJsonResponseException> (fun () ->
            StratumClient.Deserialize<BlockchainTransactionGetResult> """{"error":"bad tx_hash","id":0,"jsonrpc":"2.0"}"""
            |> ignore<BlockchainTransactionGetResult>
        )

        Assert.That(ex.ErrorCode, Is.EqualTo None)

    [<Test>]
    [<Ignore "Not fixed yet">]
    member __.``hitting the NRE reported by the user``() =
        let fakeResponse = "{\"jsonrpc\":\"2.0\",\"method\":\"blockchain.relayfee\"" + String.replicate 511 " " + "\n}"

        let listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.IPv6Loopback, 0)
        listener.Server.SetSocketOption(System.Net.Sockets.SocketOptionLevel.IPv6,
                                        System.Net.Sockets.SocketOptionName.IPv6Only, false)
        listener.Start()
        let port = uint32 (listener.LocalEndpoint :?> System.Net.IPEndPoint).Port

        let serverAsync = async {
            let! client = listener.AcceptTcpClientAsync() |> Async.AwaitTask
            use stream = client.GetStream()
            use reader = new System.IO.StreamReader(stream)
            let! _request = reader.ReadLineAsync() |> Async.AwaitTask
            use writer = new System.IO.StreamWriter(stream)
            writer.AutoFlush <- true
            do! writer.WriteLineAsync(fakeResponse) |> Async.AwaitTask
        }
        let _serverTask = serverAsync |> Async.StartAsTask
        let mutable response: Option<BlockchainScriptHashGetBalanceResult> = None

        let ex =
            try
                let jsonRpcClient = JsonRpcTcpClient("localhost", port)
                let stratumClient = StratumClient(jsonRpcClient)
                async {
                    let! res = stratumClient.BlockchainScriptHashGetBalance "someaddress"
                    response <- Some res
                }
                |> Async.RunSynchronously
                |> ignore
                None
            with
            | exn -> Some exn
        match ex, response with
        | Some ex, _ ->
            let responseType = "BlockchainScriptHashGetBalance"
            let jsonFragmentInResponse = "blockchain.relayfee"
            let exceptionTypeName = ex.GetType().Name

            Assert.That(
                ex.Message.Contains responseType,
                sprintf
                    "Exception received (of type %s) didn't contain response type '%s' that the JSON has to conform to"
                    exceptionTypeName
                    responseType
            )
            Assert.That(
                ex.Message.Contains jsonFragmentInResponse,
                sprintf
                    "Exception received (of type %s) didn't contain original JSON fragment '%s'"
                    exceptionTypeName
                    jsonFragmentInResponse
            )
            let properlyTypedException = (exceptionTypeName <> "Exception")
            Assert.That(
                properlyTypedException,
                Is.True
            )
        | None, None -> Assert.Fail "Impossible to get no response and no exception"
        | None, Some res ->
            Assert.Fail (
                sprintf
                    "Should have failed, but we got a response with this Id: %s"
                    (res.Id.ToString())
            )

    [<Test>]
    member __.``End2End test of a correct interaction returning balance successfully``() =
        let balanceResponse = "{\"id\": 0, \"result\": {\"confirmed\": 12345, \"unconfirmed\": 6789}}"
        let serverVersionResponse = "{\"id\":0,\"jsonrpc\":\"2.0\",\"result\":[\"Fulcrum 2.1.1\",\"1.4\"]}"

        let listener = new Sockets.TcpListener(IPAddress.IPv6Loopback, 0)
        listener.Server.SetSocketOption(Sockets.SocketOptionLevel.IPv6,
                                        Sockets.SocketOptionName.IPv6Only, false)
        listener.Start()
        let port = uint32 (listener.LocalEndpoint :?> IPEndPoint).Port

        let serverAsync = async {
            try
                let! firstClient = listener.AcceptTcpClientAsync() |> Async.AwaitTask
                use firstStream = firstClient.GetStream()
                use firstReader = new System.IO.StreamReader(firstStream)
                let! _firstRequest = firstReader.ReadLineAsync() |> Async.AwaitTask
                use firstWriter = new System.IO.StreamWriter(firstStream)
                firstWriter.AutoFlush <- true
                do! firstWriter.WriteLineAsync(serverVersionResponse) |> Async.AwaitTask

                let! secondClient = listener.AcceptTcpClientAsync() |> Async.AwaitTask
                use secondStream = secondClient.GetStream()
                use secondReader = new System.IO.StreamReader(secondStream)
                let! _secondRequest = secondReader.ReadLineAsync() |> Async.AwaitTask
                use secondWriter = new System.IO.StreamWriter(secondStream)
                secondWriter.AutoFlush <- true
                do! secondWriter.WriteLineAsync(balanceResponse) |> Async.AwaitTask
            with
            | ex -> Console.Error.WriteLine (sprintf "Server Error: %s" (ex.ToString()))
        }
        let serverTask = serverAsync |> Async.StartAsTask

        let jsonRpcClient = JsonRpcTcpClient("localhost", port)
        let stratumClient = StratumClient(jsonRpcClient)

        try
            async {
                let! version = stratumClient.ServerVersion "geewallet" (Version "1.4")
                Assert.That(version, Is.EqualTo(Version "1.4"),
                    sprintf "Expected protocol version 1.4 but got %s" (version.ToString()))
                let! balance = stratumClient.BlockchainScriptHashGetBalance "someaddress"
                Assert.That(balance.Result.Confirmed, Is.EqualTo(12345L),
                    sprintf "Expected confirmed balance 12345 but got %d" balance.Result.Confirmed)
                Assert.That(balance.Result.Unconfirmed, Is.EqualTo(6789L),
                    sprintf "Expected unconfirmed balance 6789 but got %d" balance.Result.Unconfirmed)
            }
            |> Async.RunSynchronously
        finally
            listener.Stop()
            serverTask.Wait(1000) |> ignore
