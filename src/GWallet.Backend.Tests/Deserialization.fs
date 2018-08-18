namespace GWallet.Backend.Tests

open System

open NUnit.Framework

open GWallet.Backend

module Deserialization =

    [<Test>]
    let ``deserialize cache does not fail``() =

        let deserializedCache: DietCache = Marshalling.Deserialize
                                               MarshallingData.SofisticatedCachingDataExampleInJson

        Assert.That(deserializedCache, Is.Not.Null)

        Assert.That(deserializedCache.Addresses, Is.Not.Null)
        let etcAddresses = deserializedCache.Addresses.TryFind "0xFOOBARBAZ"
        Assert.That etcAddresses.IsSome
        let addresses = etcAddresses.Value
        Assert.That(addresses, Is.EqualTo [Currency.ETC.ToString()])

        Assert.That(deserializedCache.Balances, Is.Not.Null)
        let etcBalance = deserializedCache.Balances.TryFind (Currency.ETC.ToString())
        Assert.That(etcBalance.IsSome)
        let balance = etcBalance.Value
        Assert.That(balance, Is.EqualTo(123456789.12345678m))

        Assert.That(deserializedCache.UsdPrice, Is.Not.Null)
        let price = deserializedCache.UsdPrice.Item (Currency.ETH.ToString())
        Assert.That(price, Is.EqualTo(161.796))

    [<Test>]
    let ``unsigned btc transaction import``() =
        let deserializedUnsignedTrans: UnsignedTransaction<IBlockchainFeeInfo> =
            Account.ImportUnsignedTransactionFromJson
                MarshallingData.UnsignedBtcTransactionExampleInJson

        Assert.That(deserializedUnsignedTrans, Is.Not.Null)
        Assert.That(deserializedUnsignedTrans.Proposal, Is.Not.Null)
        Assert.That(deserializedUnsignedTrans.Cache, Is.Not.Null)
        Assert.That(deserializedUnsignedTrans.Metadata, Is.Not.Null)

        Assert.That(deserializedUnsignedTrans.Proposal.Amount.ValueToSend, Is.EqualTo(10.01m))
        Assert.That(deserializedUnsignedTrans.Proposal.Amount.BalanceAtTheMomentOfSending,
                    Is.EqualTo 12.02m)
        Assert.That(deserializedUnsignedTrans.Proposal.Amount.Currency, Is.EqualTo Currency.BTC)
        Assert.That(deserializedUnsignedTrans.Proposal.DestinationAddress,
                    Is.EqualTo("13jxHQDxGto46QhjFiMb78dZdys9ZD8vW5"))
        Assert.That(deserializedUnsignedTrans.Proposal.OriginAddress,
                    Is.EqualTo("16pKBjGGZkUXo1afyBNf5ttFvV9hauS1kR"))

        let btcTxMetadata = deserializedUnsignedTrans.Metadata :?> UtxoCoin.TransactionMetadata
        Assert.That(btcTxMetadata.Fee.EstimatedTransactionSizeInBytes, Is.EqualTo(10))
        Assert.That(btcTxMetadata.Fee.AmountPerKiloByteForFastTransaction, Is.EqualTo(0.1m))
        Assert.That(btcTxMetadata.Fee.EstimatedTransactionSizeInBytes, Is.EqualTo(10))
        Assert.That(btcTxMetadata.TransactionDraft.Inputs.Length, Is.EqualTo(1))
        Assert.That(btcTxMetadata.TransactionDraft.Outputs.Length, Is.EqualTo(1))
        Assert.That(deserializedUnsignedTrans.Metadata.FeeEstimationTime,
                    Is.EqualTo(MarshallingData.SomeDate))

        Assert.That(deserializedUnsignedTrans.Cache.Balances.Count, Is.EqualTo(0))
        Assert.That(deserializedUnsignedTrans.Cache.UsdPrice.Count, Is.EqualTo(0))

    [<Test>]
    let ``unsigned ether transaction import``() =
        let deserializedUnsignedTrans: UnsignedTransaction<IBlockchainFeeInfo> =
            Account.ImportUnsignedTransactionFromJson
                MarshallingData.UnsignedEtherTransactionExampleInJson

        Assert.That(deserializedUnsignedTrans, Is.Not.Null)
        Assert.That(deserializedUnsignedTrans.Proposal, Is.Not.Null)
        Assert.That(deserializedUnsignedTrans.Cache, Is.Not.Null)
        Assert.That(deserializedUnsignedTrans.Metadata, Is.Not.Null)

        Assert.That(deserializedUnsignedTrans.Proposal.Amount.ValueToSend, Is.EqualTo(10.01m))
        Assert.That(deserializedUnsignedTrans.Proposal.Amount.BalanceAtTheMomentOfSending,
                    Is.EqualTo 12.02m)
        Assert.That(deserializedUnsignedTrans.Proposal.Amount.Currency, Is.EqualTo Currency.ETC)
        Assert.That(deserializedUnsignedTrans.Proposal.DestinationAddress,
                    Is.EqualTo("0xf3j4m0rjxdddud9403j"))
        Assert.That(deserializedUnsignedTrans.Proposal.OriginAddress,
                    Is.EqualTo("0xf3j4m0rjx94sushh03j"))

        let etherTxMetadata = deserializedUnsignedTrans.Metadata :?> Ether.TransactionMetadata
        Assert.That(etherTxMetadata.TransactionCount, Is.EqualTo(69))
        Assert.That(etherTxMetadata.Fee.Currency, Is.EqualTo(Currency.ETC))
        Assert.That(etherTxMetadata.Fee.GasPriceInWei, Is.EqualTo(6969))
        Assert.That(deserializedUnsignedTrans.Metadata.FeeEstimationTime,
                    Is.EqualTo(MarshallingData.SomeDate))

        Assert.That(deserializedUnsignedTrans.Cache.Balances.Count, Is.EqualTo(0))
        Assert.That(deserializedUnsignedTrans.Cache.UsdPrice.Count, Is.EqualTo(0))

    [<Test>]
    let ``signed btc transaction import``() =

        let deserializedSignedTrans: SignedTransaction<IBlockchainFeeInfo> =
            Account.ImportSignedTransactionFromJson
                MarshallingData.SignedBtcTransactionExampleInJson

        Assert.That(deserializedSignedTrans, Is.Not.Null)

        Assert.That(deserializedSignedTrans.RawTransaction,
            Is.EqualTo("ropkrpork4p4rkpo4kprok4rp"))

        Assert.That(deserializedSignedTrans.TransactionInfo, Is.Not.Null)
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal, Is.Not.Null)
        Assert.That(deserializedSignedTrans.TransactionInfo.Cache, Is.Not.Null)
        Assert.That(deserializedSignedTrans.TransactionInfo.Metadata, Is.Not.Null)

        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.Amount.ValueToSend,
                    Is.EqualTo(10.01m))
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.Amount.BalanceAtTheMomentOfSending,
                    Is.EqualTo 12.02m)
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.Amount.Currency,
                    Is.EqualTo(Currency.BTC))
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.DestinationAddress,
                    Is.EqualTo("13jxHQDxGto46QhjFiMb78dZdys9ZD8vW5"))
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.OriginAddress,
                    Is.EqualTo("16pKBjGGZkUXo1afyBNf5ttFvV9hauS1kR"))

        let btcTxMetadata = deserializedSignedTrans.TransactionInfo.Metadata :?> UtxoCoin.TransactionMetadata
        Assert.That(btcTxMetadata.Fee.EstimatedTransactionSizeInBytes, Is.EqualTo(10))
        Assert.That(btcTxMetadata.Fee.AmountPerKiloByteForFastTransaction, Is.EqualTo(0.1m))
        Assert.That(btcTxMetadata.TransactionDraft.Inputs.Length, Is.EqualTo(1))
        Assert.That(btcTxMetadata.TransactionDraft.Outputs.Length, Is.EqualTo(1))
        Assert.That(deserializedSignedTrans.TransactionInfo.Metadata.FeeEstimationTime,
                    Is.EqualTo(MarshallingData.SomeDate))

        Assert.That(deserializedSignedTrans.TransactionInfo.Cache.Balances.Count,
                    Is.EqualTo(0))
        Assert.That(deserializedSignedTrans.TransactionInfo.Cache.UsdPrice.Count,
                    Is.EqualTo(0))

    [<Test>]
    let ``signed ether transaction import``() =

        let deserializedSignedTrans: SignedTransaction<IBlockchainFeeInfo> =
            Account.ImportSignedTransactionFromJson
                MarshallingData.SignedEtherTransactionExampleInJson

        Assert.That(deserializedSignedTrans, Is.Not.Null)

        Assert.That(deserializedSignedTrans.RawTransaction,
            Is.EqualTo("doijfsoifjdosisdjfomirmjosmi"))

        Assert.That(deserializedSignedTrans.TransactionInfo, Is.Not.Null)
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal, Is.Not.Null)
        Assert.That(deserializedSignedTrans.TransactionInfo.Cache, Is.Not.Null)
        Assert.That(deserializedSignedTrans.TransactionInfo.Metadata, Is.Not.Null)

        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.Amount.ValueToSend,
                    Is.EqualTo(10.01m))
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.Amount.BalanceAtTheMomentOfSending,
                    Is.EqualTo 12.02m)
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.Amount.Currency,
                    Is.EqualTo(Currency.ETC))
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.DestinationAddress,
                    Is.EqualTo("0xf3j4m0rjxdddud9403j"))
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.OriginAddress,
                    Is.EqualTo("0xf3j4m0rjx94sushh03j"))

        let etherTxMetadata = deserializedSignedTrans.TransactionInfo.Metadata :?> Ether.TransactionMetadata
        Assert.That(etherTxMetadata.TransactionCount, Is.EqualTo(69))
        Assert.That(etherTxMetadata.Fee.Currency,
                    Is.EqualTo(Currency.ETC))
        Assert.That(etherTxMetadata.Fee.GasPriceInWei,
                    Is.EqualTo(6969))
        Assert.That(deserializedSignedTrans.TransactionInfo.Metadata.FeeEstimationTime,
                    Is.EqualTo(MarshallingData.SomeDate))

        Assert.That(deserializedSignedTrans.TransactionInfo.Cache.Balances.Count,
                    Is.EqualTo(2))
        Assert.That(deserializedSignedTrans.TransactionInfo.Cache.UsdPrice.Count,
                    Is.EqualTo(2))

    [<Test>]
    let ``unsigned DAI transaction import``() =
        let deserializedUnsignedTrans: UnsignedTransaction<IBlockchainFeeInfo> =
            Account.ImportUnsignedTransactionFromJson
                MarshallingData.UnsignedDaiTransactionExampleInJson

        Assert.That(deserializedUnsignedTrans, Is.Not.Null)
        Assert.That(deserializedUnsignedTrans.Proposal, Is.Not.Null)
        Assert.That(deserializedUnsignedTrans.Cache, Is.Not.Null)
        Assert.That(deserializedUnsignedTrans.Metadata, Is.Not.Null)

        Assert.That(deserializedUnsignedTrans.Proposal.Amount.ValueToSend, Is.EqualTo(1m))
        Assert.That(deserializedUnsignedTrans.Proposal.Amount.BalanceAtTheMomentOfSending,
                    Is.EqualTo 7.08m)
        Assert.That(deserializedUnsignedTrans.Proposal.Amount.Currency, Is.EqualTo Currency.DAI)
        Assert.That(deserializedUnsignedTrans.Proposal.DestinationAddress,
                    Is.EqualTo("0xDb0381B1a380d8db2724A9Ca2d33E0C6C044bE3b"))
        Assert.That(deserializedUnsignedTrans.Proposal.OriginAddress,
                    Is.EqualTo("0xba766d6d13E2Cc921Bf6e896319D32502af9e37E"))

        let daiTxMetadata = deserializedUnsignedTrans.Metadata :?> Ether.TransactionMetadata
        Assert.That(daiTxMetadata.TransactionCount, Is.EqualTo(7))
        Assert.That(daiTxMetadata.Fee.Currency, Is.EqualTo(Currency.ETH))
        Assert.That(daiTxMetadata.Fee.GasPriceInWei, Is.EqualTo(3343750000L))
        Assert.That(deserializedUnsignedTrans.Metadata.FeeEstimationTime,
                    Is.EqualTo(DateTime.Parse("2018-03-14T16:50:09.133411")))

        Assert.That(deserializedUnsignedTrans.Cache.Balances.Count, Is.EqualTo 5)
        Assert.That(deserializedUnsignedTrans.Cache.UsdPrice.Count, Is.EqualTo(5))

    [<Test>]
    let ``signed DAI transaction import``() =

        let deserializedSignedTrans: SignedTransaction<IBlockchainFeeInfo> =
            Account.ImportSignedTransactionFromJson
                MarshallingData.SignedDaiTransactionExampleInJson

        Assert.That(deserializedSignedTrans, Is.Not.Null)

        Assert.That(deserializedSignedTrans.RawTransaction,
            Is.EqualTo("f8a80784c74d93708291b29489d24a6b4ccb1b6faa2625fe562bdd9a2326035980b844a9059cbb000000000000000000000000db0381b1a380d8db2724a9ca2d33e0c6c044be3b0000000000000000000000000000000000000000000000000de0b6b3a764000026a072cdeb03affd5977c76366efbc1405fbb4fa997ce72c1e4554ba9ec5ef772ddca069d522ea304efebd2537330870bc1ca9e9a6fe3eb5f8d8f66c1b82d9fc27a4bf"))

        Assert.That(deserializedSignedTrans.TransactionInfo, Is.Not.Null)
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal, Is.Not.Null)
        Assert.That(deserializedSignedTrans.TransactionInfo.Cache, Is.Not.Null)
        Assert.That(deserializedSignedTrans.TransactionInfo.Metadata, Is.Not.Null)

        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.Amount.ValueToSend,
                    Is.EqualTo(1m))
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.Amount.BalanceAtTheMomentOfSending,
                    Is.EqualTo 7.08m)
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.Amount.Currency,
                    Is.EqualTo(Currency.DAI))
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.DestinationAddress,
                    Is.EqualTo("0xDb0381B1a380d8db2724A9Ca2d33E0C6C044bE3b"))
        Assert.That(deserializedSignedTrans.TransactionInfo.Proposal.OriginAddress,
                    Is.EqualTo("0xba766d6d13E2Cc921Bf6e896319D32502af9e37E"))

        let etherTxMetadata = deserializedSignedTrans.TransactionInfo.Metadata :?> Ether.TransactionMetadata
        Assert.That(etherTxMetadata.TransactionCount, Is.EqualTo(7))
        Assert.That(etherTxMetadata.Fee.Currency,
                    Is.EqualTo(Currency.ETH))
        Assert.That(etherTxMetadata.Fee.GasPriceInWei,
                    Is.EqualTo(3343750000L))
        Assert.That(deserializedSignedTrans.TransactionInfo.Metadata.FeeEstimationTime,
                    Is.EqualTo(DateTime.Parse("2018-03-14T16:50:09.133411")))

        Assert.That(deserializedSignedTrans.TransactionInfo.Cache.Balances.Count,
                    Is.EqualTo 5)
        Assert.That(deserializedSignedTrans.TransactionInfo.Cache.UsdPrice.Count,
                    Is.EqualTo(5))
