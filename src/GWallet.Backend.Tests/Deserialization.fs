namespace GWallet.Backend.Tests

open System
open System.Numerics

open NUnit.Framework
open Newtonsoft.Json

open GWallet.Backend

module Deserialization =

    [<Test>]
    let ``deserialize cache does not fail``() =

        let deserializedCache = Caching.ImportFromJson
                                    MarshallingData.SofisticatedCachingDataExampleInJson

        Assert.That(deserializedCache, Is.Not.Null)

        Assert.That(deserializedCache.Balances, Is.Not.Null)
        Assert.That(deserializedCache.Balances.ContainsKey("0xFOOBARBAZ"))
        let balance,date = deserializedCache.Balances.Item "0xFOOBARBAZ"
        Assert.That(balance, Is.EqualTo(123456789.12345678m))
        Assert.That(date, Is.EqualTo (MarshallingData.SomeDate))

        Assert.That(deserializedCache.UsdPrice, Is.Not.Null)
        let price,date = deserializedCache.UsdPrice.Item Currency.ETH
        Assert.That(price, Is.EqualTo(161.796))
        Assert.That(date, Is.EqualTo (MarshallingData.SomeDate))

    [<Test>]
    let ``unsigned transaction import``() =
        let deserializedUnsignedTrans =
            Account.ImportUnsignedTransactionFromJson
                MarshallingData.UnsignedTransactionExampleInJson

        Assert.That(deserializedUnsignedTrans, Is.Not.Null)
        Assert.That(deserializedUnsignedTrans.Proposal, Is.Not.Null)
        Assert.That(deserializedUnsignedTrans.Cache, Is.Not.Null)
        Assert.That(deserializedUnsignedTrans.Fee, Is.Not.Null)

        Assert.That(deserializedUnsignedTrans.Proposal.Amount.ValueToSend, Is.EqualTo(10.01m))
        Assert.That(deserializedUnsignedTrans.Proposal.Amount.IdealValueRemainingAfterSending,
                    Is.EqualTo(1.01m))
        Assert.That(deserializedUnsignedTrans.Proposal.Currency, Is.EqualTo(Currency.ETC))
        Assert.That(deserializedUnsignedTrans.Proposal.DestinationAddress,
                    Is.EqualTo("0xf3j4m0rjxdddud9403j"))
        Assert.That(deserializedUnsignedTrans.Proposal.OriginAddress,
                    Is.EqualTo("0xf3j4m0rjx94sushh03j"))

        Assert.That(deserializedUnsignedTrans.TransactionCount, Is.EqualTo(69))

        Assert.That(deserializedUnsignedTrans.Fee.Currency, Is.EqualTo(Currency.ETC))
        Assert.That(deserializedUnsignedTrans.Fee.GasPriceInWei, Is.EqualTo(6969))
        Assert.That((deserializedUnsignedTrans.Fee:>IBlockchainFee).EstimationTime,
                    Is.EqualTo(MarshallingData.SomeDate))

        Assert.That(deserializedUnsignedTrans.Cache.Balances.Count, Is.EqualTo(0))
        Assert.That(deserializedUnsignedTrans.Cache.UsdPrice.Count, Is.EqualTo(0))

    [<Test>]
    let ``signed transaction import``() =

        let deserializedSignedTrans =
            Account.ImportSignedTransactionFromJson
                MarshallingData.SignedTransactionExampleInJson

        Assert.That(deserializedSignedTrans, Is.Not.Null)

        Assert.That(deserializedSignedTrans.RawTransaction,
            Is.EqualTo("doijfsoifjdosisdjfomirmjosmi"))

        Assert.That(deserializedSignedTrans.TransactionInfo, Is.Not.Null)
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal, Is.Not.Null)
        Assert.That(deserializedSignedTrans.TransactionInfo.Cache, Is.Not.Null)
        Assert.That(deserializedSignedTrans.TransactionInfo.Fee, Is.Not.Null)

        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.Amount.ValueToSend,
                    Is.EqualTo(10.01m))
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.Amount.IdealValueRemainingAfterSending,
                    Is.EqualTo(1.01m))
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.Currency,
                    Is.EqualTo(Currency.ETC))
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.DestinationAddress,
                    Is.EqualTo("0xf3j4m0rjxdddud9403j"))
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.OriginAddress,
                    Is.EqualTo("0xf3j4m0rjx94sushh03j"))

        Assert.That(deserializedSignedTrans.TransactionInfo.TransactionCount, Is.EqualTo(69))

        Assert.That(deserializedSignedTrans.TransactionInfo.Fee.Currency,
                    Is.EqualTo(Currency.ETC))
        Assert.That(deserializedSignedTrans.TransactionInfo.Fee.GasPriceInWei,
                    Is.EqualTo(6969))
        Assert.That((deserializedSignedTrans.TransactionInfo.Fee:>IBlockchainFee).EstimationTime,
                    Is.EqualTo(MarshallingData.SomeDate))

        Assert.That(deserializedSignedTrans.TransactionInfo.Cache.Balances.Count,
                    Is.EqualTo(2))
        Assert.That(deserializedSignedTrans.TransactionInfo.Cache.UsdPrice.Count,
                    Is.EqualTo(2))
