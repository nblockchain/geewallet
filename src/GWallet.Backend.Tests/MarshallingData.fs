﻿namespace GWallet.Backend.Tests

open System
open System.IO
open System.Reflection

open NUnit.Framework
open Newtonsoft.Json.Linq

open GWallet.Backend
open GWallet.Backend.UtxoCoin
open GWallet.Backend.Ether

module MarshallingData =

    let private executingAssembly = Assembly.GetExecutingAssembly()
    let private version = VersionHelper.CURRENT_VERSION
    let private binPath = executingAssembly.Location |> FileInfo
    let private prjPath = Path.Combine(binPath.Directory.FullName, "..") |> DirectoryInfo

    let private RemoveJsonFormatting (jsonContent: string): string =
        jsonContent.Replace("\r", String.Empty)
                   .Replace("\n", String.Empty)
                   .Replace("\t", String.Empty)

    let private InjectCurrentVersion (jsonContent: string): string =
        jsonContent.Replace("{version}", version)

    let private InjectCurrentDir (jsonContent: string): string =
        jsonContent.Replace("{prjDirAbsolutePath}", prjPath.FullName.Replace("\\", "/"))

    let internal Sanitize =
        RemoveJsonFormatting
        >> InjectCurrentVersion
        >> InjectCurrentDir

    let private ReadEmbeddedResource resourceName =
        Fsdk.Misc.ExtractEmbeddedResourceFileContents resourceName
        |> Sanitize

    let UnsignedSaiTransactionExampleInJson =
        ReadEmbeddedResource "unsignedAndFormattedSaiTransaction.json"

    let SignedDaiTransactionExampleInJson =
        ReadEmbeddedResource "signedAndFormattedDaiTransaction.json"

    let BasicExceptionExampleInJson =
        ReadEmbeddedResource "basicException.json"

    let RealExceptionExampleInJson =
        ReadEmbeddedResource "realException.json"

    let RealExceptionUnixLegacyExampleInJson =
        ReadEmbeddedResource "realException_unixLegacy.json"

    let RealExceptionWindowsLegacyExampleInJson =
        ReadEmbeddedResource "realException_windowsLegacy.json"

    let InnerExceptionExampleInJson =
        ReadEmbeddedResource "innerException.json"

    let CustomExceptionExampleInJson =
        ReadEmbeddedResource "customException.json"

    let CustomFSharpExceptionExampleInJson =
        ReadEmbeddedResource "customFSharpException.json"

    let CustomFSharpExceptionLegacyExampleInJson =
        ReadEmbeddedResource "customFSharpException_legacy.json"

    let FullExceptionExampleInJson =
        ReadEmbeddedResource "fullException.json"

    let FullExceptionUnixLegacyExampleInJson =
        ReadEmbeddedResource "fullException_unixLegacy.json"

    let FullExceptionWindowsLegacyExampleInJson =
        ReadEmbeddedResource "fullException_windowsLegacy.json"

    let rec TrimOutsideAndInside(str: string) =
        let trimmed = str.Replace("  ", " ").Trim()
        if trimmed = str then
            trimmed
        else
            TrimOutsideAndInside trimmed

    let SerializedExceptionsAreSame actualJsonString expectedJsonString ignoreExMessage msg =

        let actualJsonException = JObject.Parse actualJsonString
        let expectedJsonException = JObject.Parse expectedJsonString

        let fullBinaryFormPath = "Value.FullBinaryForm"
        let tweakStackTraces () =

            let fullBinaryFormBeginning = "AAEAAAD/////AQAA"
            let stackTracePath = "Value.HumanReadableSummary.StackTrace"
            let stackTraceFragment = "ExceptionMarshalling.fs"

            let tweakStackTraceAndBinaryForm (jsonEx: JObject) (assertBinaryForm: bool) =
                let stackTraceJToken = jsonEx.SelectToken stackTracePath
                Assert.That(stackTraceJToken, Is.Not.Null, sprintf "Path %s not found in %s" stackTracePath (jsonEx.ToString()))
                let initialStackTraceJToken = stackTraceJToken.ToString()
                if initialStackTraceJToken.Length > 0 then
                    Assert.That(initialStackTraceJToken, IsString.WhichContains stackTraceFragment,
                                sprintf "comparing actual '%s' with expected '%s'" actualJsonString expectedJsonString)
                    let endOfStackTrace = initialStackTraceJToken.Substring(initialStackTraceJToken.IndexOf stackTraceFragment)
                    let tweakedEndOfStackTrace =
                        endOfStackTrace
                            .Replace(":line 42", ":41 ")
                            .Replace(":line 41", ":41 ")
                            .Replace(":line 65", ":64 ")
                            .Replace(":line 64", ":64 ")
                    stackTraceJToken.Replace (tweakedEndOfStackTrace |> JToken.op_Implicit)

                let binaryFormToken = jsonEx.SelectToken fullBinaryFormPath
                Assert.That(binaryFormToken, Is.Not.Null, sprintf "Path %s not found in %s" fullBinaryFormPath (jsonEx.ToString()))
                let initialBinaryFormJToken = binaryFormToken.ToString()
                if assertBinaryForm then
                    Assert.That(initialBinaryFormJToken, IsString.StartingWith fullBinaryFormBeginning)
                binaryFormToken.Replace (fullBinaryFormBeginning |> JToken.op_Implicit)

            tweakStackTraceAndBinaryForm actualJsonException true
            tweakStackTraceAndBinaryForm expectedJsonException false

        tweakStackTraces()

        // strangely enough, message would be different between linux_vanilla_dotnet6 and other dotnet6 configs (e.g. Windows, macOS, Linux-github)
        if ignoreExMessage then
            let exMessagePath = "Value.HumanReadableSummary.Message"
            let actualMsgToken = actualJsonException.SelectToken exMessagePath
            Assert.That(actualMsgToken, Is.Not.Null, sprintf "Path %s not found in %s" exMessagePath (actualJsonException.ToString()))
            let expectedMsgToken = expectedJsonException.SelectToken exMessagePath
            Assert.That(expectedMsgToken, Is.Not.Null, sprintf "Path %s not found in %s" exMessagePath (expectedJsonException.ToString()))
            actualMsgToken.Replace(String.Empty |> JToken.op_Implicit)
            expectedMsgToken.Replace(String.Empty |> JToken.op_Implicit)

        let actualBinaryForm = (actualJsonException.SelectToken fullBinaryFormPath).ToString()
        Assert.That(
            TrimOutsideAndInside(actualJsonException.ToString()),
            Is.EqualTo (TrimOutsideAndInside(expectedJsonException.ToString())),
            msg + actualBinaryForm
        )

        true

    let internal SomeDate = DateTime.Parse "2018-06-14T16:50:09.133411"

    let private someEtherMinerFee =
        Ether.MinerFee(21000L, 6969L, SomeDate, Currency.ETC)

    let private someUnsignedEtherTransactionProposal =
        {
            OriginAddress = "0xf3j4m0rjx94sushh03j";
            Amount = TransferAmount(10.01m, 12.02m, Currency.ETC);
            DestinationAddress = "0xf3j4m0rjxdddud9403j";
        }

    let EmptyCachingDataExample =
        { UsdPrice = Map.empty; Addresses = Map.empty; Balances = Map.empty; }

    let EmptyCachingDataExampleInJson =
        sprintf """{
  "Version": "%s",
  "TypeName": "%s",
  "Value": {
    "UsdPrice": {},
    "Addresses": {},
    "Balances": {}
  }
}"""        version (EmptyCachingDataExample.GetType().FullName)

    let private balances = Map.empty.Add(Currency.BTC.ToString(), 0m)
                                    .Add(Currency.ETC.ToString(), 123456789.12345678m)
    let private addresses = Map.empty.Add("1fooBarBaz", [Currency.BTC.ToString()])
                                     .Add("0xFOOBARBAZ", [Currency.ETC.ToString()])
    let private fiatValues = Map.empty.Add(Currency.ETH.ToString(), 161.796m)
                                      .Add(Currency.ETC.ToString(), 169.99999999m)
    let SofisticatedCachingDataExample = { UsdPrice = fiatValues; Addresses = addresses; Balances = balances; }

    let SofisticatedCachingDataExampleInJson =
        sprintf """{
  "Version": "%s",
  "TypeName": "%s",
  "Value": {
    "UsdPrice": {
      "ETC": 169.99999999,
      "ETH": 161.796
    },
    "Addresses": {
      "0xFOOBARBAZ": [
        "ETC"
      ],
      "1fooBarBaz": [
        "BTC"
      ]
    },
    "Balances": {
      "BTC": 0.0,
      "ETC": 123456789.12345678
    }
  }
}"""        version (typedefof<DietCache>.FullName)

    let private someUnsignedBtcTransactionProposal =
        {
            OriginAddress = "16pKBjGGZkUXo1afyBNf5ttFvV9hauS1kR";
            Amount = TransferAmount(10.01m, 12.02m, Currency.BTC);
            DestinationAddress = "13jxHQDxGto46QhjFiMb78dZdys9ZD8vW5";
        }

    let private someBtcTransactionInputs =
        [ { TransactionHash = "4d129e98d87fab00a99ebc88688752b588ec7d38c2ba5dc86d3563a6bc4c691f"
            OutputIndex = 1
            ValueInSatoshis = int64 1000
            DestinationInHex = "a9145131075257d8b8de8298e7c52891eb4b87823b9387" } ]

    let private realUsdPriceDataSample =
        [ (Currency.BTC.ToString(), 9156.19m);
          (Currency.LTC.ToString(), 173.592m);
          (Currency.ETH.ToString(), 691.52m);
          (Currency.ETC.ToString(), 19.8644m);
          (Currency.SAI.ToString(), 1.00376m) ]
            |> Map.ofSeq

    let private realAddressesSample =
        Map.empty.Add("3Buz1evVsQeHtDfQAmwfAKQsUzAt3f4TuR",[Currency.BTC.ToString()])
                 .Add("0xba766d6d13E2Cc921Bf6e896319D32502af9e37E",[Currency.ETH.ToString();
                                                                    Currency.SAI.ToString()
                                                                    Currency.ETC.ToString()])
                 .Add("MJ88KYLTpXVigiwJGevzyxfGogmKx7WiWm",[Currency.LTC.ToString()])

    let private realBalancesDataSample =
        Map.empty.Add(Currency.BTC.ToString(), 0.0m)
                 .Add(Currency.ETH.ToString(), 7.08m)
                 .Add(Currency.ETC.ToString(), 8.0m)
                 .Add(Currency.SAI.ToString(), 1.0m)
                 .Add(Currency.LTC.ToString(), 0.0m)

    let private realCachingDataExample =
        { UsdPrice = realUsdPriceDataSample; Addresses = realAddressesSample; Balances = realBalancesDataSample; }

    let private someBtcMinerFee = UtxoCoin.MinerFee(10L, SomeDate, Currency.BTC)
    let private someBtcTxMetadata =
        {
            Fee = someBtcMinerFee;
            Inputs = someBtcTransactionInputs
        }
    let UnsignedBtcTransactionExample =
        {
            Proposal = someUnsignedBtcTransactionProposal;
            Cache = realCachingDataExample;
            Metadata = someBtcTxMetadata;
        }

    let UnsignedBtcTransactionExampleInJson =
        ReadEmbeddedResource "unsignedAndFormattedBtcTransaction.json"

    let SignedBtcTransactionExample =
        {
            Currency = Currency.BTC
            FeeCurrency = Currency.BTC 
            RawTransaction = "01000000000102cd9e4c06746721fe5d0ecdeabe29a0f05cc22bd7013ff76132efe476d9346bdc0000000017160014618869483590d6c1afe51160f244982e055d213ffdffffffef2763e4690975dc9415d36c06361ddee8393e6d9d86edd748ca21f10788fbc30100000017160014618869483590d6c1afe51160f244982e055d213ffdffffff01ba89000000000000220020574712746ca1942b8f0e3d52e4c1fd9406c3e1b602b328a2a77a57c233fed4640247304402206e9359074007c597a8243d4e5bbfb18ccfd83c0206fcbd1fafc02eb4946852f90220566e0d719b48d11f193d5d6d80eccbaaf44ee1771bf9ea7fd3810d41c5cb429f012102b7326aff8f2e56a341c31fbf50d0ce1a641859d837daffd7bf03f1f80a8c5eaa0247304402202fdbb2ea123c1150b26835ecd54cd59a22bca6a47f32167b35f355fbfcc12d22022011b8314e51b229d6d5a5ee216c9e038b5e05d1b5123485b935a1f823f2bf2279012102b7326aff8f2e56a341c31fbf50d0ce1a641859d837daffd7bf03f1f80a8c5eaa00000000";
        }

    let SignedBtcTransactionExampleInJson =
        ReadEmbeddedResource "signedAndFormattedBtcTransaction.json"

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

    let private someEtherMinerFeeForSaiTransfer = Ether.MinerFee(37298L,
                                                                 3343750000L,
                                                                 SomeDate,
                                                                 Currency.ETH)
    let private someSaiTxMetadata =
        {
            Fee = someEtherMinerFeeForSaiTransfer
            TransactionCount = int64 7;
        }
    let private someUnsignedSaiTransactionProposal =
        {
            OriginAddress = "0xba766d6d13E2Cc921Bf6e896319D32502af9e37E";
            Amount = TransferAmount(1m, 7.08m, Currency.SAI)
            DestinationAddress = "0xDb0381B1a380d8db2724A9Ca2d33E0C6C044bE3b";
        }
    let UnsignedSaiTransactionExample =
        {
            Proposal = someUnsignedSaiTransactionProposal
            Cache = realCachingDataExample;
            Metadata = someSaiTxMetadata
        }
    let someSaiTransactionInfo =
        {
            Proposal = someUnsignedSaiTransactionProposal
            Cache = realCachingDataExample;
            Metadata = someSaiTxMetadata
        }
    let SignedDaiTransactionExample =
        {
            Currency = Currency.DAI
            FeeCurrency = Currency.ETH 
            RawTransaction = "f8a90185016c653675828792946b175474e89094c44da98b954eedeac495271d0f80b844a9059cbb000000000000000000000000d2fdfa29d5ccbb8168ba248d59ded7a25396f84e0000000000000000000000000000000000000000000000000de0b6b3a764000026a0d5c49133f38f3b60aa41747a4b7cc300a6dac87803b82ba23af9a97fd5994c3ea03122864fd6b294a3da2f3827e70fa861838a168f6533e03587358a6bdc594235";
        }

    let someEtherTransactionInfo =
        {
            Proposal = someUnsignedEtherTransactionProposal;
            Cache = SofisticatedCachingDataExample;
            Metadata = someEtherTxMetadata;
        }
    let SignedEtherTransactionExample =
        {
            Currency = Currency.ETH
            FeeCurrency = Currency.ETH 
            RawTransaction = "f86b0185019d334a3482520894d2fdfa29d5ccbb8168ba248d59ded7a25396f84e87022a8ad81f98768026a06bb7c1f8f2b40ed2bc3a3b572cdde7fddb42a8d43c561c60580183b0ed8c2d9fa035183359feab8789642135a253371f80781f4a870f0cae8a7368c5d7e102a688";
        }
    let SignedEtherTransactionExampleInJson =
        ReadEmbeddedResource "signedAndFormattedEtherTransaction.json"

    let UnsignedEtherTransactionExampleInJson =
        ReadEmbeddedResource "unsignedAndFormattedEtherTransaction.json"
