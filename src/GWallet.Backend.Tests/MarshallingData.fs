namespace GWallet.Backend.Tests

open System
open System.Reflection

open Newtonsoft.Json

open GWallet.Backend

module MarshallingData =
    let version = Assembly.GetExecutingAssembly().GetName().Version.ToString()

    let SomeDate = DateTime.Now

    let private someEthMinerFee = EtherMinerFee(int64 6969, SomeDate, Currency.ETC)

    let private someUnsignedTransactionProposal =
        {
            Currency = Currency.ETC;
            OriginAddress = "0xf3j4m0rjx94sushh03j";
            Amount = 10.01m;
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


    let private someTransInfo =
        {
            Proposal = someUnsignedTransactionProposal;
            TransactionCount = int64 69;
            Cache = SofisticatedCachindDataExample;
            Fee = someEthMinerFee;
        }

    let UnsignedTransactionExample =
        {
            Proposal = someUnsignedTransactionProposal;
            TransactionCount = int64 69;
            Cache = EmptyCachingDataExample;
            Fee = someEthMinerFee;
        }

    let SignedTransactionExample =
        {
            TransactionInfo = someTransInfo;
            RawTransaction = "doijfsoifjdosisdjfomirmjosmi";
        }
    let SignedTransactionExampleInJson =
        (sprintf "{\"Version\":\"%s\"," version) +
        (sprintf "\"TypeName\":\"%s\",\"Value\":" (SignedTransactionExample.GetType().FullName)) +
        "{\"TransactionInfo\":{\"Proposal\":" +
        "{\"Currency\":{\"Case\":\"ETC\"}," +
        "\"OriginAddress\":\"0xf3j4m0rjx94sushh03j\"," +
        "\"Amount\":10.01," +
        "\"DestinationAddress\":\"0xf3j4m0rjxdddud9403j\"}" +
        ",\"TransactionCount\":69," +
        "\"Fee\":{" +
        "\"GasPriceInWei\":6969,"+
        "\"Currency\":{\"Case\":\"ETC\"}," +
        "\"EstimationTime\":" +
        JsonConvert.SerializeObject (SomeDate) + "}," +
        "\"Cache\":" + innerCachingDataForSofisticatedUseCase + "}," +
        "\"RawTransaction\":\"doijfsoifjdosisdjfomirmjosmi\"}}"

    let UnsignedTransactionExampleInJson =
        (sprintf "{\"Version\":\"%s\"," version) +
        (sprintf "\"TypeName\":\"%s\",\"Value\":" (UnsignedTransactionExample.GetType().FullName)) +
        "{\"Proposal\":" +
        "{\"Currency\":{\"Case\":\"ETC\"}," +
        "\"OriginAddress\":\"0xf3j4m0rjx94sushh03j\"," +
        "\"Amount\":10.01," +
        "\"DestinationAddress\":\"0xf3j4m0rjxdddud9403j\"}" +
        ",\"TransactionCount\":69," +
        "\"Fee\":{" +
        "\"GasPriceInWei\":6969," +
        "\"Currency\":{\"Case\":\"ETC\"}," +
        "\"EstimationTime\":" +
        JsonConvert.SerializeObject(SomeDate) + "}," +
        "\"Cache\":{\"UsdPrice\":{},\"Balances\":{}}}}"