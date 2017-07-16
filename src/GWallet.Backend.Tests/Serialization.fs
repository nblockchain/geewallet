namespace GWallet.Backend.Tests

open System
open System.Numerics

open NUnit.Framework
open Newtonsoft.Json

open GWallet.Backend

module Serialization =

    [<Test>]
    let ``basic caching export does not fail``() =
        let someCachingData = { UsdPrice = Map.empty; Balances = Map.empty }
        let json = Caching.ExportToJson(Some(someCachingData))
        Assert.That(json, Is.Not.Null)
        Assert.That(json, Is.Not.Empty)

    [<Test>]
    let ``basic caching export is accurate``() =
        let someCachingData = { UsdPrice = Map.empty; Balances = Map.empty }
        let json = Caching.ExportToJson(Some(someCachingData))
        Assert.That(json, Is.Not.Null)
        Assert.That(json, Is.Not.Empty)
        Assert.That(json, Is.EqualTo("{\"UsdPrice\":{},\"Balances\":{}}"))

    [<Test>]
    let ``complex caching export works``() =
        let someDate = DateTime.Now
        let balances = Map.empty.Add("1foeijfeoiherji", (0m, someDate))
                                .Add("0x3894348932998", (123456789.12345678m, someDate))
        let fiatValues = Map.empty.Add(Currency.ETH, (169.99999999m, someDate))
                                  .Add(Currency.ETC, (169.99999999m, someDate))
        let someCachingData = { UsdPrice = fiatValues; Balances = balances }
        let json = Caching.ExportToJson(Some(someCachingData))
        Assert.That(json, Is.Not.Null)
        Assert.That(json, Is.Not.Empty)

        Assert.That(json,
                    Is.EqualTo(
                        String.Format ("{{\"UsdPrice\":{{" +
                                       "\"ETH\":{{\"Item1\":169.99999999,\"Item2\":{0}}}," +
                                       "\"ETC\":{{\"Item1\":169.99999999,\"Item2\":{0}}}" +
                                       "}},\"Balances\":{{" +
                                       "\"0x3894348932998\":{{\"Item1\":123456789.12345678,\"Item2\":{0}}}," +
                                       "\"1foeijfeoiherji\":{{\"Item1\":0.0,\"Item2\":{0}}}" +
                                       "}}}}",
                                       JsonConvert.SerializeObject(someDate))
                    )
                   )

    [<Test>]
    let ``unsigned transaction export``() =
        let someDate = DateTime.Now
        let someEthMinerFee =
            {
                GasPriceInWei = int64 69;
                EstimationTime = someDate;
                Currency = Currency.ETC;
            }
        let someUnsignedTransactionProposal =
            {
                Currency = Currency.ETC;
                OriginAddress = "0xf3j4m0rjx94sushh03j";
                Amount = 10m;
                DestinationAddress = "0xf3j4m0rjxdddud9403j";
            }
        let someUnsignedTrans =
            {
                Proposal = someUnsignedTransactionProposal;
                TransactionCount = int64 69;
                Cache = { UsdPrice = Map.empty; Balances = Map.empty };
                Fee = someEthMinerFee;
            }
        let someCachingData = { UsdPrice = Map.empty; Balances = Map.empty }
        let json = Account.ExportUnsignedTransactionToJson someUnsignedTrans
        Assert.That(json, Is.Not.Null)
        Assert.That(json, Is.Not.Empty)
        Assert.That(json,
                    Is.EqualTo(
                        "{\"Proposal\":" +
                        "{\"Currency\":\"ETC\"," +
                        "\"OriginAddress\":\"0xf3j4m0rjx94sushh03j\"," +
                        "\"Amount\":10.0," +
                        "\"DestinationAddress\":\"0xf3j4m0rjxdddud9403j\"}" +
                        ",\"TransactionCount\":69," +
                        "\"Fee\":{" +
                        "\"GasPriceInWei\":69," +
                        "\"EstimationTime\":" +
                        JsonConvert.SerializeObject(someDate) +
                        ",\"Currency\":\"ETC\"}," +
                        "\"Cache\":{\"UsdPrice\":{},\"Balances\":{}}}"))
