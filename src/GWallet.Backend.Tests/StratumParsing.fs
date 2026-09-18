namespace GWallet.Backend.Tests

open System

open NUnit.Framework

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
                printfn "Confirmed balance received from NRE test: " + response.Result.Confirmed
                None
            with
            | exn -> Some exn
        match ex, response with
        | Some ex, _ ->
            Assert.That(ex.Message.Contains "blockchain.relayfee")
        | None, None -> Assert.Fail "Impossible to get no response and no exception"
        | None, Some res ->
            Assert.Fail (
                sprintf "Should have failed, but we got a response with this data: Id=%s,Conf=%s,Unconf=%s"
                (res.Id.ToString())
                (res.Result.Confirmed)
                (res.Result.Unconfirmed)
            )

