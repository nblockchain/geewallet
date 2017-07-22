namespace GWallet.Backend.Tests

open System
open System.Numerics

open NUnit.Framework
open Newtonsoft.Json

open GWallet.Backend

module Deserialization =

    [<Test>]
    let ``deserialize cache does not fail``() =
        let someDate = DateTime.Now

        let cacheData =
            "{\"UsdPrice\":{\"ETH\":{\"Item1\":161.796,\"Item2\":" +
            JsonConvert.SerializeObject(someDate) +
            "}},\"Balances\":{\"0xFOOBARBAZ\":{\"Item1\":96.69,\"Item2\":" +
            JsonConvert.SerializeObject(someDate) + "}}}"
        let deserializedCache = Caching.ImportFromJson (cacheData)

        Assert.That(deserializedCache, Is.Not.Null)

        Assert.That(deserializedCache.Balances, Is.Not.Null)
        Assert.That(deserializedCache.Balances.ContainsKey("0xFOOBARBAZ"))
        let balance,date = deserializedCache.Balances.Item "0xFOOBARBAZ"
        Assert.That(balance, Is.EqualTo(96.69))
        Assert.That(date, Is.EqualTo(someDate))

        Assert.That(deserializedCache.UsdPrice, Is.Not.Null)
        let price,date = deserializedCache.UsdPrice.Item Currency.ETH
        Assert.That(price, Is.EqualTo(161.796))
        Assert.That(date, Is.EqualTo(someDate))

    [<Test>]
    let ``unsigned transaction import``() =
        let someDate = DateTime.Now

        let unsignedTransInJson =
            "{\"Proposal\":" +
            "{\"Currency\":\"ETC\"," +
            "\"OriginAddress\":\"0xf3j4m0rjx94sushh03j\"," +
            "\"Amount\":10.01," +
            "\"DestinationAddress\":\"0xf3j4m0rjxdddud9403j\"}" +
            ",\"TransactionCount\":69," +
            "\"Fee\":{" +
            "\"GasPriceInWei\":6969," +
            "\"EstimationTime\":" +
            JsonConvert.SerializeObject(someDate) +
            ",\"Currency\":\"ETC\"}," +
            "\"Cache\":{\"UsdPrice\":{},\"Balances\":{}}}"

        let deserializedUnsignedTrans =
            Account.ImportUnsignedTransactionFromJson unsignedTransInJson
        Assert.That(deserializedUnsignedTrans, Is.Not.Null)
        Assert.That(deserializedUnsignedTrans.Proposal, Is.Not.Null)
        Assert.That(deserializedUnsignedTrans.Cache, Is.Not.Null)
        Assert.That(deserializedUnsignedTrans.Fee, Is.Not.Null)

        Assert.That(deserializedUnsignedTrans.Proposal.Amount, Is.EqualTo(10.01m))
        Assert.That(deserializedUnsignedTrans.Proposal.Currency, Is.EqualTo(Currency.ETC))
        Assert.That(deserializedUnsignedTrans.Proposal.DestinationAddress,
                    Is.EqualTo("0xf3j4m0rjxdddud9403j"))
        Assert.That(deserializedUnsignedTrans.Proposal.OriginAddress,
                    Is.EqualTo("0xf3j4m0rjx94sushh03j"))

        Assert.That(deserializedUnsignedTrans.TransactionCount, Is.EqualTo(69))

        Assert.That(deserializedUnsignedTrans.Fee.Currency, Is.EqualTo(Currency.ETC))
        Assert.That(deserializedUnsignedTrans.Fee.GasPriceInWei, Is.EqualTo(6969))
        Assert.That(deserializedUnsignedTrans.Fee.EstimationTime, Is.EqualTo(someDate))

        Assert.That(deserializedUnsignedTrans.Cache.Balances.Count, Is.EqualTo(0))
        Assert.That(deserializedUnsignedTrans.Cache.UsdPrice.Count, Is.EqualTo(0))
