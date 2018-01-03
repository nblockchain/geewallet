namespace GWallet.Backend.Tests

open NUnit.Framework
open Newtonsoft.Json

open GWallet.Backend.Bitcoin

module StratumParsing =

    [<Test>]
    let ``deserialize a successful balance response``() =
        let getBalanceRequest = {
            Id = 0;
            Method = "blockchain.address.get_balance";
            Params = ["someBtcAddress-irrelevantForThisTest"]
        }
        let json = JsonConvert.SerializeObject(getBalanceRequest, Formatting.None, StratumClient.GetDefaultJsonSerializationSettings())

        let balanceResponse = "{\"id\": 1, \"result\": {\"confirmed\": 1, \"unconfirmed\": 2}}"
        
        let balance = StratumClient.Deserialize<BlockchainAddressGetBalanceResult>(balanceResponse, json)
        Assert.That(balance.Result.Confirmed, Is.EqualTo(1))
        Assert.That(balance.Result.Unconfirmed, Is.EqualTo(2))

    [<Test>]
    let ``deserialize a stratum error from an electrum client doesn't fail``() =
        let getBalanceRequest = {
            Id = 0;
            Method = "blockchain.address.get_balance";
            Params = ["someBtcAddress-irrelevantForThisTest"]
        }
        let json = JsonConvert.SerializeObject(getBalanceRequest, Formatting.None, StratumClient.GetDefaultJsonSerializationSettings())

        let errorResponse = "{\"jsonrpc\": \"2.0\", \"id\": 0, \"error\": {\"message\": \"internal error processing request\", \"code\": -32603}}"
        
        let ex = Assert.Throws<ElectrumServerReturningInternalErrorInJsonResponseException>(fun _ ->
            StratumClient.Deserialize<BlockchainAddressGetBalanceResult>(errorResponse, json) |> ignore
        )
        Assert.That(ex.ErrorCode, Is.EqualTo(-32603))