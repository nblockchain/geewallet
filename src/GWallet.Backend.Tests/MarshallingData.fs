namespace GWallet.Backend.Tests

open System
open System.IO
open System.Reflection

open Newtonsoft.Json

open GWallet.Backend
open GWallet.Backend.UtxoCoin
open GWallet.Backend.Ether

module MarshallingData =

    let internal RemoveJsonFormatting (jsonContent: string): string =
        jsonContent.Replace("\r", String.Empty)
                   .Replace("\n", String.Empty)
                   .Replace("\t", String.Empty)

    let private ReadEmbeddedResource resourceName =
        let assembly = Assembly.GetExecutingAssembly()
        use stream = assembly.GetManifestResourceStream resourceName
        if (stream = null) then
            failwithf "Embedded resource %s not found" resourceName
        use reader = new StreamReader(stream)
        reader.ReadToEnd() |> RemoveJsonFormatting

    let UnsignedDaiTransactionExampleInJson =
        ReadEmbeddedResource "unsignedAndFormattedDaiTransaction.json"

    let SignedDaiTransactionExampleInJson =
        ReadEmbeddedResource "signedAndFormattedDaiTransaction.json"

    let version = Assembly.GetExecutingAssembly().GetName().Version.ToString()

    let SomeDate = DateTime.UtcNow

    let private someEtherMinerFee = Ether.MinerFee(21000L, 6969L, SomeDate, Currency.ETC)

    let private someUnsignedEtherTransactionProposal =
        {
            OriginAddress = "0xf3j4m0rjx94sushh03j";
            Amount = TransferAmount(10.01m, 12.02m, Currency.ETC);
            DestinationAddress = "0xf3j4m0rjxdddud9403j";
        }

    let EmptyCachingDataExample =
        { UsdPrice = Map.empty; Addresses = Map.empty; Balances = Map.empty; }

    let EmptyCachingDataExampleInJson =
        sprintf "{\"Version\":\"%s\",\"TypeName\":\"%s\","
                version (EmptyCachingDataExample.GetType().FullName) +
                "\"Value\":{\"UsdPrice\":{},\"Addresses\":{},\"Balances\":{}}}"

    let private balances = Map.empty.Add(Currency.BTC.ToString(), 0m)
                                    .Add(Currency.ETC.ToString(), 123456789.12345678m)
    let private addresses = Map.empty.Add("1fooBarBaz", [Currency.BTC.ToString()])
                                     .Add("0xFOOBARBAZ", [Currency.ETC.ToString()])
    let private fiatValues = Map.empty.Add(Currency.ETH.ToString(), 161.796m)
                                      .Add(Currency.ETC.ToString(), 169.99999999m)
    let SofisticatedCachingDataExample = { UsdPrice = fiatValues; Addresses = addresses; Balances = balances; }

    let private innerCachingDataForSofisticatedUseCase =
        "{\"UsdPrice\":{\"ETC\":169.99999999" +
        ",\"ETH\":161.796" +
        "},\"Addresses\":{\"0xFOOBARBAZ\":[\"ETC\"],\"1fooBarBaz\":[\"BTC\"]}," +
        "\"Balances\":{\"BTC\":0.0," +
        "\"ETC\":123456789.12345678}}"

    let SofisticatedCachingDataExampleInJson =
        (sprintf "{\"Version\":\"%s\",\"TypeName\":\"%s\","
                 version (typedefof<DietCache>.FullName)) +
                 "\"Value\":" + innerCachingDataForSofisticatedUseCase +
                 "}"

    let private someUnsignedBtcTransactionProposal =
        {
            OriginAddress = "16pKBjGGZkUXo1afyBNf5ttFvV9hauS1kR";
            Amount = TransferAmount(10.01m, 12.02m, Currency.BTC);
            DestinationAddress = "13jxHQDxGto46QhjFiMb78dZdys9ZD8vW5";
        }

    let private someBtcTransactionDraft =
        {
            Inputs = [ { TransactionHash = "xyzt...";
                         OutputIndex = 1;
                         ValueInSatoshis = int64 1000;
                         DestinationInHex = "0123456789ABCD" } ];
            Outputs = [ { ValueInSatoshis = int64 10000; DestinationAddress = "13jxHQDxGto46QhjFiMb78dZdys9ZD8vW5" } ];
        }
    let private someBtcMinerFee = UtxoCoin.MinerFee(10, 0.1m, SomeDate, Currency.BTC)
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
        "{" +
        "\"OriginAddress\":\"16pKBjGGZkUXo1afyBNf5ttFvV9hauS1kR\"," +
        "\"Amount\":{\"ValueToSend\":10.01,\"BalanceAtTheMomentOfSending\":12.02,\"Currency\":{\"Case\":\"BTC\"}}," +
        "\"DestinationAddress\":\"13jxHQDxGto46QhjFiMb78dZdys9ZD8vW5\"}," +
        "\"Metadata\":{\"Fee\":" +
        "{\"EstimatedTransactionSizeInBytes\":10," +
        "\"AmountPerKiloByteForFastTransaction\":0.1," +
        "\"EstimationTime\":" + JsonConvert.SerializeObject (SomeDate) + ","+
        "\"Currency\":{\"Case\":\"BTC\"}}," +
        "\"TransactionDraft\":{\"Inputs\":" +
        "[{\"TransactionHash\":\"xyzt...\",\"OutputIndex\":1,\"ValueInSatoshis\":1000" +
        ",\"DestinationInHex\":\"0123456789ABCD\"}]," +
        "\"Outputs\":[{\"ValueInSatoshis\":10000,\"DestinationAddress\":\"13jxHQDxGto46QhjFiMb78dZdys9ZD8vW5\"}]}}," +
        "\"Cache\":{\"UsdPrice\":{},\"Addresses\":{},\"Balances\":{}}}}"

    let SignedBtcTransactionExample =
        {
            TransactionInfo = UnsignedBtcTransactionExample;
            RawTransaction = "ropkrpork4p4rkpo4kprok4rp";
        }

    let SignedBtcTransactionExampleInJson =
        (sprintf "{\"Version\":\"%s\"," version) +
        (sprintf "\"TypeName\":\"%s\",\"Value\":" (SignedBtcTransactionExample.GetType().FullName)) +
        "{\"TransactionInfo\":{\"Proposal\":" +
        "{" +
        "\"OriginAddress\":\"16pKBjGGZkUXo1afyBNf5ttFvV9hauS1kR\"," +
        "\"Amount\":{\"ValueToSend\":10.01,\"BalanceAtTheMomentOfSending\":12.02,\"Currency\":{\"Case\":\"BTC\"}}," +
        "\"DestinationAddress\":\"13jxHQDxGto46QhjFiMb78dZdys9ZD8vW5\"}," +
        "\"Metadata\":{\"Fee\":{" +
        "\"EstimatedTransactionSizeInBytes\":10," +
        "\"AmountPerKiloByteForFastTransaction\":0.1," +
        "\"EstimationTime\":" + JsonConvert.SerializeObject (SomeDate) + "," +
        "\"Currency\":{\"Case\":\"BTC\"}}," +

        "\"TransactionDraft\":{\"Inputs\":" +
        "[{\"TransactionHash\":\"xyzt...\",\"OutputIndex\":1,\"ValueInSatoshis\":1000," +
        "\"DestinationInHex\":\"0123456789ABCD\"}]," +
        "\"Outputs\":[{\"ValueInSatoshis\":10000,\"DestinationAddress\":\"13jxHQDxGto46QhjFiMb78dZdys9ZD8vW5\"}]}}," +
        "\"Cache\":{\"UsdPrice\":{},\"Addresses\":{},\"Balances\":{}}}," +
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

    let private realUsdPriceDataSample =
        [ (Currency.BTC.ToString(), 9156.19m);
          (Currency.LTC.ToString(), 173.592m);
          (Currency.ETH.ToString(), 691.52m);
          (Currency.ETC.ToString(), 19.8644m);
          (Currency.DAI.ToString(), 1.00376m); ]
            |> Map.ofSeq

    let private realAddressesSample =
        Map.empty.Add("3Buz1evVsQeHtDfQAmwfAKQsUzAt3f4TuR",[Currency.BTC.ToString()])
                 .Add("0xba766d6d13E2Cc921Bf6e896319D32502af9e37E",[Currency.ETH.ToString();
                                                                    Currency.DAI.ToString();
                                                                    Currency.ETC.ToString()])
                 .Add("MJ88KYLTpXVigiwJGevzyxfGogmKx7WiWm",[Currency.LTC.ToString()])

    let private realBalancesDataSample =
        Map.empty.Add(Currency.BTC.ToString(), 0.0m)
                 .Add(Currency.ETH.ToString(), 7.08m)
                 .Add(Currency.ETC.ToString(), 8.0m)
                 .Add(Currency.DAI.ToString(), 1.0m)
                 .Add(Currency.LTC.ToString(), 0.0m)

    let private realCachingDataExample =
        { UsdPrice = realUsdPriceDataSample; Addresses = realAddressesSample; Balances = realBalancesDataSample; }

    let private someEtherMinerFeeForDaiTransfer = Ether.MinerFee(37298L,
                                                                 3343750000L,
                                                                 DateTime.Parse "2018-03-14T16:50:09.133411",
                                                                 Currency.ETH)
    let private someDaiTxMetadata =
        {
            Fee = someEtherMinerFeeForDaiTransfer;
            TransactionCount = int64 7;
        }
    let private someUnsignedDaiTransactionProposal =
        {
            OriginAddress = "0xba766d6d13E2Cc921Bf6e896319D32502af9e37E";
            Amount = TransferAmount(1m, 7.08m, Currency.DAI);
            DestinationAddress = "0xDb0381B1a380d8db2724A9Ca2d33E0C6C044bE3b";
        }
    let UnsignedDaiTransactionExample =
        {
            Proposal = someUnsignedDaiTransactionProposal;
            Cache = realCachingDataExample;
            Metadata = someDaiTxMetadata;
        }
    let someDaiTransactionInfo =
        {
            Proposal = someUnsignedDaiTransactionProposal;
            Cache = realCachingDataExample;
            Metadata = someDaiTxMetadata;
        }
    let SignedDaiTransactionExample =
        {
            TransactionInfo = someDaiTransactionInfo;
            RawTransaction = "f8a80784c74d93708291b29489d24a6b4ccb1b6faa2625fe562bdd9a2326035980b844a9059cbb000000000000000000000000db0381b1a380d8db2724a9ca2d33e0c6c044be3b0000000000000000000000000000000000000000000000000de0b6b3a764000026a072cdeb03affd5977c76366efbc1405fbb4fa997ce72c1e4554ba9ec5ef772ddca069d522ea304efebd2537330870bc1ca9e9a6fe3eb5f8d8f66c1b82d9fc27a4bf";
        }

    let someEtherTransactionInfo =
        {
            Proposal = someUnsignedEtherTransactionProposal;
            Cache = SofisticatedCachingDataExample;
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
        "{" +
        "\"OriginAddress\":\"0xf3j4m0rjx94sushh03j\"," +
        "\"Amount\":{\"ValueToSend\":10.01,\"BalanceAtTheMomentOfSending\":12.02,\"Currency\":{\"Case\":\"ETC\"}}," +
        "\"DestinationAddress\":\"0xf3j4m0rjxdddud9403j\"}" +
        ",\"Metadata\":{" +
        "\"Fee\":{" +
        "\"GasLimit\":21000," +
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
        "{" +
        "\"OriginAddress\":\"0xf3j4m0rjx94sushh03j\"," +
        "\"Amount\":{\"ValueToSend\":10.01,\"BalanceAtTheMomentOfSending\":12.02,\"Currency\":{\"Case\":\"ETC\"}}," +
        "\"DestinationAddress\":\"0xf3j4m0rjxdddud9403j\"}" +
        ",\"Metadata\":{" +
        "\"Fee\":{" +
        "\"GasLimit\":21000," +
        "\"GasPriceInWei\":6969," +
        "\"Currency\":{\"Case\":\"ETC\"}," +
        "\"EstimationTime\":" +
        JsonConvert.SerializeObject(SomeDate) + "}," +
        "\"TransactionCount\":69}," +
        "\"Cache\":{\"UsdPrice\":{},\"Addresses\":{},\"Balances\":{}}}}"
