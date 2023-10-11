namespace GWallet.Backend.Tests

open System

open NUnit.Framework

open GWallet.Backend
open GWallet.Backend.UtxoCoin

[<TestFixture>]
type Deserialization() =

    [<Test>]
    member __.``deserialize cache does not fail``() =

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
    member __.``unsigned btc transaction import``() =
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
        Assert.That(btcTxMetadata.Fee.EstimatedFeeInSatoshis, Is.EqualTo 10)
        Assert.That(btcTxMetadata.Inputs.Length, Is.EqualTo 1)
        Assert.That(deserializedUnsignedTrans.Metadata.FeeEstimationTime,
                    Is.EqualTo MarshallingData.SomeDate)

        Assert.That(deserializedUnsignedTrans.Cache.Balances.Count, Is.EqualTo 5)
        Assert.That(deserializedUnsignedTrans.Cache.UsdPrice.Count, Is.EqualTo 5)

    [<Test>]
    member __.``unsigned ether transaction import``() =
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
                    Is.EqualTo MarshallingData.SomeDate)

        Assert.That(deserializedUnsignedTrans.Cache.Balances.Count, Is.EqualTo(0))
        Assert.That(deserializedUnsignedTrans.Cache.UsdPrice.Count, Is.EqualTo(0))

    [<Test>]
    member __.``signed btc transaction import``() =

        let deserializedSignedTrans: SignedTransaction =
            Account.ImportSignedTransactionFromJson
                MarshallingData.SignedBtcTransactionExampleInJson

        // Validate deserialized SignedTranasction        
        Assert.That(deserializedSignedTrans, Is.Not.Null)

        Assert.That(deserializedSignedTrans.Currency, Is.EqualTo Currency.BTC)
        Assert.That(deserializedSignedTrans.FeeCurrency, Is.EqualTo Currency.BTC)
        Assert.That(deserializedSignedTrans.RawTransaction,
            Is.EqualTo "01000000000102cd9e4c06746721fe5d0ecdeabe29a0f05cc22bd7013ff76132efe476d9346bdc0000000017160014618869483590d6c1afe51160f244982e055d213ffdffffffef2763e4690975dc9415d36c06361ddee8393e6d9d86edd748ca21f10788fbc30100000017160014618869483590d6c1afe51160f244982e055d213ffdffffff01ba89000000000000220020574712746ca1942b8f0e3d52e4c1fd9406c3e1b602b328a2a77a57c233fed4640247304402206e9359074007c597a8243d4e5bbfb18ccfd83c0206fcbd1fafc02eb4946852f90220566e0d719b48d11f193d5d6d80eccbaaf44ee1771bf9ea7fd3810d41c5cb429f012102b7326aff8f2e56a341c31fbf50d0ce1a641859d837daffd7bf03f1f80a8c5eaa0247304402202fdbb2ea123c1150b26835ecd54cd59a22bca6a47f32167b35f355fbfcc12d22022011b8314e51b229d6d5a5ee216c9e038b5e05d1b5123485b935a1f823f2bf2279012102b7326aff8f2e56a341c31fbf50d0ce1a641859d837daffd7bf03f1f80a8c5eaa00000000")

        // Can't validate proposal because of "unknown origin account" error
        
        let btcTxMetadata =
            Account.GetTransactionMetadata deserializedSignedTrans
            |> Async.RunSynchronously
            :?> UtxoCoin.TransactionMetadata
           
        Assert.That(btcTxMetadata.Fee.EstimatedFeeInSatoshis, Is.EqualTo 980)
        Assert.That(btcTxMetadata.Inputs.Length, Is.EqualTo 2)
    
    [<Test>]
    member __.``signed ether transaction import``() =

        let deserializedSignedTrans: SignedTransaction =
            Account.ImportSignedTransactionFromJson
                MarshallingData.SignedEtherTransactionExampleInJson

        // Validate deserialized SignedTransaction
        Assert.That(deserializedSignedTrans, Is.Not.Null)

        Assert.That(deserializedSignedTrans.RawTransaction,
            Is.EqualTo("f86b0185019d334a3482520894d2fdfa29d5ccbb8168ba248d59ded7a25396f84e87022a8ad81f98768026a06bb7c1f8f2b40ed2bc3a3b572cdde7fddb42a8d43c561c60580183b0ed8c2d9fa035183359feab8789642135a253371f80781f4a870f0cae8a7368c5d7e102a688"))

        Assert.That(deserializedSignedTrans.Currency,
                    Is.EqualTo(Currency.ETH))
        
        Assert.That(deserializedSignedTrans.FeeCurrency,
                    Is.EqualTo(Currency.ETH))

        
        // Validate generated proposal
        let proposal = Account.GetTransactionProposal deserializedSignedTrans
        
        Assert.That(proposal.Amount.ValueToSend,
            Is.EqualTo(0.000609725773224054m))
        Assert.That(proposal.Amount.Currency,
                    Is.EqualTo(Currency.ETH))
        Assert.That(proposal.DestinationAddress,
                    Is.EqualTo("0xd2FDFA29D5ccbb8168Ba248D59dED7a25396f84E"))
        Assert.That(proposal.OriginAddress,
                    Is.EqualTo("0xc295DDB9B89AFb7B0b23cFb76cb34ce33bc854D5"))
       
        // Validate generated metadata
        let etherTxMetadata =
            Account.GetTransactionMetadata deserializedSignedTrans
            |> Async.RunSynchronously
            :?> Ether.TransactionMetadata
            
        Assert.That(etherTxMetadata.TransactionCount, Is.EqualTo(1))
        Assert.That(etherTxMetadata.Fee.Currency,
                    Is.EqualTo(Currency.ETH))
        Assert.That(etherTxMetadata.Fee.GasPriceInWei,
                    Is.EqualTo(6932351540UL))

    [<Test>]
    member __.``unsigned SAI transaction import``() =
        let deserializedUnsignedTrans: UnsignedTransaction<IBlockchainFeeInfo> =
            Account.ImportUnsignedTransactionFromJson
                MarshallingData.UnsignedSaiTransactionExampleInJson

        Assert.That(deserializedUnsignedTrans, Is.Not.Null)
        Assert.That(deserializedUnsignedTrans.Proposal, Is.Not.Null)
        Assert.That(deserializedUnsignedTrans.Cache, Is.Not.Null)
        Assert.That(deserializedUnsignedTrans.Metadata, Is.Not.Null)

        Assert.That(deserializedUnsignedTrans.Proposal.Amount.ValueToSend, Is.EqualTo(1m))
        Assert.That(deserializedUnsignedTrans.Proposal.Amount.BalanceAtTheMomentOfSending,
                    Is.EqualTo 7.08m)
        Assert.That(deserializedUnsignedTrans.Proposal.Amount.Currency, Is.EqualTo Currency.SAI)
        Assert.That(deserializedUnsignedTrans.Proposal.DestinationAddress,
                    Is.EqualTo("0xDb0381B1a380d8db2724A9Ca2d33E0C6C044bE3b"))
        Assert.That(deserializedUnsignedTrans.Proposal.OriginAddress,
                    Is.EqualTo("0xba766d6d13E2Cc921Bf6e896319D32502af9e37E"))

        let saiTxMetadata = deserializedUnsignedTrans.Metadata :?> Ether.TransactionMetadata
        Assert.That(saiTxMetadata.TransactionCount, Is.EqualTo(7))
        Assert.That(saiTxMetadata.Fee.Currency, Is.EqualTo(Currency.ETH))
        Assert.That(saiTxMetadata.Fee.GasPriceInWei, Is.EqualTo(3343750000L))
        Assert.That(deserializedUnsignedTrans.Metadata.FeeEstimationTime,
                    Is.EqualTo MarshallingData.SomeDate)

        Assert.That(deserializedUnsignedTrans.Cache.Balances.Count, Is.EqualTo 5)
        Assert.That(deserializedUnsignedTrans.Cache.UsdPrice.Count, Is.EqualTo(5))

    [<Test>]
    member __.``signed DAI transaction import``() =

        let deserializedSignedTrans: SignedTransaction =
            Account.ImportSignedTransactionFromJson
                MarshallingData.SignedDaiTransactionExampleInJson

        // Validate deserialized SignedTransaction
        Assert.That(deserializedSignedTrans, Is.Not.Null)

        Assert.That(deserializedSignedTrans.RawTransaction,
            Is.EqualTo("f8a90185016c653675828792946b175474e89094c44da98b954eedeac495271d0f80b844a9059cbb000000000000000000000000d2fdfa29d5ccbb8168ba248d59ded7a25396f84e0000000000000000000000000000000000000000000000000de0b6b3a764000026a0d5c49133f38f3b60aa41747a4b7cc300a6dac87803b82ba23af9a97fd5994c3ea03122864fd6b294a3da2f3827e70fa861838a168f6533e03587358a6bdc594235"))

        Assert.That(deserializedSignedTrans.Currency,
                    Is.EqualTo Currency.DAI)
        
        Assert.That(deserializedSignedTrans.FeeCurrency,
                    Is.EqualTo(Currency.ETH))
        
        // Validate generated proposal
        let proposal = Account.GetTransactionProposal deserializedSignedTrans
        
        Assert.That(proposal.Amount.ValueToSend,
            Is.EqualTo(1.0m))
        Assert.That(proposal.Amount.Currency,
                    Is.EqualTo(Currency.DAI))
        Assert.That(proposal.DestinationAddress,
                    Is.EqualTo("0xd2FDFA29D5ccbb8168Ba248D59dED7a25396f84E"))
        Assert.That(proposal.OriginAddress,
                    Is.EqualTo("0xc295DDB9B89AFb7B0b23cFb76cb34ce33bc854D5"))

        // Validate generated metadata
        let etherTxMetadata =
            Account.GetTransactionMetadata deserializedSignedTrans
            |> Async.RunSynchronously
            :?> Ether.TransactionMetadata
            
        Assert.That(etherTxMetadata.TransactionCount, Is.EqualTo(1))
        Assert.That(etherTxMetadata.Fee.Currency,
                    Is.EqualTo(Currency.ETH))
        Assert.That(etherTxMetadata.Fee.GasPriceInWei,
                    Is.EqualTo(6113539701UL))


    [<Test>]
    member __.``can roundtrip currency``() =
        let c = Currency.BTC
        let serialized = Marshalling.Serialize c
        let deserialized = Marshalling.Deserialize<Currency> serialized
        Assert.That(c, Is.EqualTo deserialized)
