namespace GWallet.Backend.Tests

open NUnit.Framework

open GWallet.Backend.UtxoCoin

module StratumParsing =

    [<Test>]
    let ``deserialize a successful balance response``() =
        let balanceResponse = "{\"id\": 1, \"result\": {\"confirmed\": 1, \"unconfirmed\": 2}}"
        
        let balance = StratumClient.Deserialize<BlockchainAddressGetBalanceResult> balanceResponse
        Assert.That(balance.Result.Confirmed, Is.EqualTo(1))
        Assert.That(balance.Result.Unconfirmed, Is.EqualTo(2))

    [<Test>]
    let ``deserialize a stratum error from an electrum client doesn't fail``() =
        let errorResponse = "{\"jsonrpc\": \"2.0\", \"id\": 0, \"error\": {\"message\": \"internal error processing request\", \"code\": -32603}}"
        
        let ex = Assert.Throws<ElectrumServerReturningErrorInJsonResponseException>(fun _ ->
            StratumClient.Deserialize<BlockchainAddressGetBalanceResult> errorResponse |> ignore
        )
        Assert.That(ex.ErrorCode, Is.EqualTo(-32603))