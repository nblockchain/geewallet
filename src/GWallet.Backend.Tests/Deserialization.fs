namespace GWallet.Backend.Tests

open System
open System.Numerics

open NUnit.Framework
open Newtonsoft.Json

open GWallet.Backend

module Deserialization =

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
        Assert.That(deserializedUnsignedTrans.Fee.GasPriceInWei, Is.EqualTo(6969m))
        Assert.That(deserializedUnsignedTrans.Fee.EstimationTime, Is.EqualTo(someDate))

        Assert.That(deserializedUnsignedTrans.Cache.Balances.Count, Is.EqualTo(0))
        Assert.That(deserializedUnsignedTrans.Cache.UsdPrice.Count, Is.EqualTo(0))
