namespace GWallet.Backend.Tests

open System
open System.Reflection

open Newtonsoft.Json

open GWallet.Backend
open GWallet.Backend.UtxoCoin
open GWallet.Backend.Ether

module MarshallingData =
    let version = Assembly.GetExecutingAssembly().GetName().Version.ToString()

    let SomeDate = DateTime.Now

    let private someEtherMinerFee = Ether.MinerFee(int64 6969, SomeDate, Currency.ETC)

    let private someUnsignedEtherTransactionProposal =
        {
            Currency = Currency.ETC;
            OriginAddress = "0xf3j4m0rjx94sushh03j";
            Amount = TransferAmount(10.01m, 1.01m);
            DestinationAddress = "0xf3j4m0rjxdddud9403j";
        }

    let EmptyCachingDataExample =
        { UsdPrice = Map.empty; Balances = Map.empty }

    let EmptyCachingDataExampleInJson =
        sprintf "{\"Version\":\"%s\",\"TypeName\":\"%s\","
                version (EmptyCachingDataExample.GetType().FullName) +
                "\"Value\":{\"UsdPrice\":{},\"Balances\":{}}}"

    let private balances = Map.empty.Add("1fooBarBaz", (0m, SomeDate))
                            .Add("0xFOOBARBAZ", (123456789.12345678m, SomeDate))
    let private fiatValues = Map.empty.Add(Currency.ETH, (161.796m, SomeDate))
                              .Add(Currency.ETC, (169.99999999m, SomeDate))
    let SofisticatedCachindDataExample = { UsdPrice = fiatValues; Balances = balances }

    let private innerCachingDataForSofisticatedUseCase =
        "{\"UsdPrice\":{\"ETH\":{\"Item1\":161.796,\"Item2\":" +
        JsonConvert.SerializeObject (SomeDate) +
        "},\"ETC\":{\"Item1\":169.99999999,\"Item2\":" +
        JsonConvert.SerializeObject (SomeDate) +
        "}},\"Balances\":{\"0xFOOBARBAZ\":{\"Item1\":123456789.12345678,\"Item2\":" +
        JsonConvert.SerializeObject (SomeDate) + "}" +
        ",\"1fooBarBaz\":{\"Item1\":0.0,\"Item2\":" +
        JsonConvert.SerializeObject (SomeDate) + "}}}"

    let SofisticatedCachingDataExampleInJson =
        (sprintf "{\"Version\":\"%s\",\"TypeName\":\"%s\","
                 version (typedefof<CachedNetworkData>.FullName)) +
                 "\"Value\":" + innerCachingDataForSofisticatedUseCase +
                 "}"

    let private someUnsignedBtcTransactionProposal =
        {
            Currency = Currency.BTC;
            OriginAddress = "16pKBjGGZkUXo1afyBNf5ttFvV9hauS1kR";
            Amount = TransferAmount(10.01m, 1.01m);
            DestinationAddress = "13jxHQDxGto46QhjFiMb78dZdys9ZD8vW5";
        }

    let private someBtcTransactionDraft =
        {
            Inputs = [ { RawTransaction = "xyzt..."; OutputIndex = 1 } ];
            Outputs = [ { ValueInSatoshis = int64 10000; DestinationAddress = "13jxHQDxGto46QhjFiMb78dZdys9ZD8vW5" } ];
        }
    let private someBtcMinerFee = UtxoCoin.MinerFee(10, 0.1m, SomeDate)
    let private someBtcTxMetadata =
        {
            TransactionDraft = someBtcTransactionDraft;
            Fee = someBtcMinerFee;
        }
    let UnsignedBtcTransactionExample =
        {
            Proposal = someUnsignedBtcTransactionProposal;
            Cache = EmptyCachingDataExample;
            Metadata = someBtcTxMetadata;
        }

    let UnsignedBtcTransactionExampleInJson =
        (sprintf "{\"Version\":\"%s\"," version) +
        (sprintf "\"TypeName\":\"%s\",\"Value\":" (UnsignedBtcTransactionExample.GetType().FullName)) +
        "{\"Proposal\":" +
        "{\"Currency\":{\"Case\":\"BTC\"}," +
        "\"OriginAddress\":\"16pKBjGGZkUXo1afyBNf5ttFvV9hauS1kR\"," +
        "\"Amount\":{\"ValueToSend\":10.01,\"IdealValueRemainingAfterSending\":1.01}," +
        "\"DestinationAddress\":\"13jxHQDxGto46QhjFiMb78dZdys9ZD8vW5\"}," +
        "\"Metadata\":{\"Fee\":" +
        "{\"EstimatedTransactionSizeInBytes\":10," +
        "\"AmountPerKiloByteForFastTransaction\":0.1," +
        "\"EstimationTime\":" + JsonConvert.SerializeObject (SomeDate) + "}," +
        "\"TransactionDraft\":{\"Inputs\":" +
        "[{\"RawTransaction\":\"xyzt...\",\"OutputIndex\":1}]," +
        "\"Outputs\":[{\"ValueInSatoshis\":10000,\"DestinationAddress\":\"13jxHQDxGto46QhjFiMb78dZdys9ZD8vW5\"}]}}," +
        "\"Cache\":{\"UsdPrice\":{},\"Balances\":{}}}}"

    let SignedBtcTransactionExample =
        {
            TransactionInfo = UnsignedBtcTransactionExample;
            RawTransaction = "ropkrpork4p4rkpo4kprok4rp";
        }

    let SignedBtcTransactionExampleInJson =
        (sprintf "{\"Version\":\"%s\"," version) +
        (sprintf "\"TypeName\":\"%s\",\"Value\":" (SignedBtcTransactionExample.GetType().FullName)) +
        "{\"TransactionInfo\":{\"Proposal\":" +
        "{\"Currency\":{\"Case\":\"BTC\"}," +
        "\"OriginAddress\":\"16pKBjGGZkUXo1afyBNf5ttFvV9hauS1kR\"," +
        "\"Amount\":{\"ValueToSend\":10.01,\"IdealValueRemainingAfterSending\":1.01}," +
        "\"DestinationAddress\":\"13jxHQDxGto46QhjFiMb78dZdys9ZD8vW5\"}," +
        "\"Metadata\":{\"Fee\":{" +
        "\"EstimatedTransactionSizeInBytes\":10," +
        "\"AmountPerKiloByteForFastTransaction\":0.1," +
        "\"EstimationTime\":" + JsonConvert.SerializeObject (SomeDate) + "}," +
        "\"TransactionDraft\":{\"Inputs\":" +
        "[{\"RawTransaction\":\"xyzt...\",\"OutputIndex\":1}]," +
        "\"Outputs\":[{\"ValueInSatoshis\":10000,\"DestinationAddress\":\"13jxHQDxGto46QhjFiMb78dZdys9ZD8vW5\"}]}}," +
        "\"Cache\":{\"UsdPrice\":{},\"Balances\":{}}}," +
        "\"RawTransaction\":\"ropkrpork4p4rkpo4kprok4rp\"}}"

    let private someEtherTxMetadata =
        {
            Fee = someEtherMinerFee;
            TransactionCount = int64 69;
        }
    let UnsignedEtherTransactionExample =
        {
            Proposal = someUnsignedEtherTransactionProposal;
            Cache = EmptyCachingDataExample;
            Metadata = someEtherTxMetadata;
        }
    let someEtherTransactionInfo =
        {
            Proposal = someUnsignedEtherTransactionProposal;
            Cache = SofisticatedCachindDataExample;
            Metadata = someEtherTxMetadata;
        }
    let SignedEtherTransactionExample =
        {
            TransactionInfo = someEtherTransactionInfo;
            RawTransaction = "doijfsoifjdosisdjfomirmjosmi";
        }
    let SignedEtherTransactionExampleInJson =
        (sprintf "{\"Version\":\"%s\"," version) +
        (sprintf "\"TypeName\":\"%s\",\"Value\":" (SignedEtherTransactionExample.GetType().FullName)) +
        "{\"TransactionInfo\":{\"Proposal\":" +
        "{\"Currency\":{\"Case\":\"ETC\"}," +
        "\"OriginAddress\":\"0xf3j4m0rjx94sushh03j\"," +
        "\"Amount\":{\"ValueToSend\":10.01,\"IdealValueRemainingAfterSending\":1.01}," +
        "\"DestinationAddress\":\"0xf3j4m0rjxdddud9403j\"}" +
        ",\"Metadata\":{" +
        "\"Fee\":{" +
        "\"GasPriceInWei\":6969," +
        "\"Currency\":{\"Case\":\"ETC\"}," +
        "\"EstimationTime\":" +
        JsonConvert.SerializeObject (SomeDate) + "}," +
        "\"TransactionCount\":69}," +
        "\"Cache\":" + innerCachingDataForSofisticatedUseCase + "}," +
        "\"RawTransaction\":\"doijfsoifjdosisdjfomirmjosmi\"}}"

    let UnsignedEtherTransactionExampleInJson =
        (sprintf "{\"Version\":\"%s\"," version) +
        (sprintf "\"TypeName\":\"%s\",\"Value\":" (UnsignedEtherTransactionExample.GetType().FullName)) +
        "{\"Proposal\":" +
        "{\"Currency\":{\"Case\":\"ETC\"}," +
        "\"OriginAddress\":\"0xf3j4m0rjx94sushh03j\"," +
        "\"Amount\":{\"ValueToSend\":10.01,\"IdealValueRemainingAfterSending\":1.01}," +
        "\"DestinationAddress\":\"0xf3j4m0rjxdddud9403j\"}" +
        ",\"Metadata\":{" +
        "\"Fee\":{" +
        "\"GasPriceInWei\":6969," +
        "\"Currency\":{\"Case\":\"ETC\"}," +
        "\"EstimationTime\":" +
        JsonConvert.SerializeObject(SomeDate) + "}," +
        "\"TransactionCount\":69}," +
        "\"Cache\":{\"UsdPrice\":{},\"Balances\":{}}}}"