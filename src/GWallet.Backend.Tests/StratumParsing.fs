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
        Assert.That(balance.Id, Is.EqualTo("1"))
        Assert.That(balance.Result.Confirmed, Is.EqualTo(1))
        Assert.That(balance.Result.Unconfirmed, Is.EqualTo(2))

    [<Test>]
    member __.``deserialize a balance response with a string id``() =
        let balanceResponse = """{"id":"usr-896934642","result":{"confirmed":1,"unconfirmed":2}}"""
        let balance = StratumClient.Deserialize<BlockchainScriptHashGetBalanceResult> balanceResponse
        Assert.That(balance.Id, Is.EqualTo("usr-896934642"))
        Assert.That(balance.Result.Confirmed, Is.EqualTo(1))

    [<Test>]
    member __.``deserialize a string id in both forms of stratum error``() =
        let responses =
            [ """{"id":"usr-896934642","error":{"message":"failure","code":-32603}}"""
              """{"id":"usr-896934642","error":"failure"}""" ]
        for response in responses do
            let ex = Assert.Throws<ElectrumServerReturningErrorInJsonResponseException>(fun _ ->
                StratumClient.Deserialize<BlockchainTransactionGetResult> response |> ignore
            )
            Assert.That(ex.Message, Is.EqualTo("failure"))

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


