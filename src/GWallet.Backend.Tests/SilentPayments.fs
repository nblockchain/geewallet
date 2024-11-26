namespace GWallet.Backend.Tests

open System.IO
open System.Reflection
open System.Text.Json

open NUnit.Framework
open NBitcoin

open GWallet.Backend.UtxoCoin


type private TestInput =
    {
        TxId: string
        Vout: int
        ScriptSig: string
        TxInWitness: string
        ScriptPubKey: string
        PrivateKey: string
    }
    static member FromJsonElement(jsonElement: JsonElement) =
        {
            TxId = jsonElement.GetProperty("txid").GetString()
            Vout = jsonElement.GetProperty("vout").GetInt32()
            ScriptSig = jsonElement.GetProperty("scriptSig").GetString()
            TxInWitness = jsonElement.GetProperty("txinwitness").GetString()
            ScriptPubKey = jsonElement.GetProperty("prevout").GetProperty("scriptPubKey").GetProperty("hex").GetString()
            PrivateKey = jsonElement.GetProperty("private_key").GetString()
        }

[<TestFixture>]
type SilentPayments() =
    
    let executingAssembly = Assembly.GetExecutingAssembly()
    let binPath = executingAssembly.Location |> FileInfo
    let projectDirPath = Path.Combine(binPath.Directory.FullName, "..", "..", "..")
    let dataDir = Path.Combine(projectDirPath, "data") |> DirectoryInfo

    [<Test>]
    member __.``Test creating outputs using test vectors from BIP-352``() =
        // https://github.com/bitcoin/bips/blob/master/bip-0352/send_and_receive_test_vectors.json

        let testVectorsFileName = "send_and_receive_test_vectors.json"
        let testVectorsJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDir.FullName, testVectorsFileName)))

        for testCase in testVectorsJson.RootElement.EnumerateArray() do
            let testCaseName = testCase.GetProperty("comment").GetString()
            let sending = testCase.GetProperty("sending").[0]
            let expectedOutputs = 
                sending.GetProperty("expected").GetProperty("outputs").EnumerateArray() 
                    |> Seq.map (fun each -> each.EnumerateArray() |> Seq.toArray)
                    |> Seq.toArray
            let given = sending.GetProperty "given"
            let inputs = given.GetProperty("vin").EnumerateArray() |> Seq.map TestInput.FromJsonElement |> Seq.toList
            let recipients = 
                given.GetProperty("recipients").EnumerateArray()
                    |> Seq.map (fun each -> each.GetString() |> SilentPaymentAddress.Decode)
                    |> Seq.toList
            
            if expectedOutputs.Length > 1 || (expectedOutputs.Length = 1 && expectedOutputs.[0].Length > 1) || recipients.Length > 1 then
                printfn "Skipping BIP-352 test case '%s'" testCaseName
            else
                printfn "Running BIP-352 test case '%s'" testCaseName

                let expectedOutput = expectedOutputs.[0] |> Array.tryHead |> Option.map (fun elem -> elem.GetString())
                
                let spInputs =
                    inputs
                    |> List.map (
                        fun input -> 
                            let witness = 
                                match input.TxInWitness with
                                | "" -> None
                                | hex -> 
                                    let stream = BitcoinStream(DataEncoders.Encoders.Hex.DecodeData hex)
                                    Some <| WitScript.Load stream
                            let spInput =
                                SilentPayments.ConvertToSilentPaymentInput 
                                    (Script.FromHex input.ScriptPubKey) 
                                    (DataEncoders.Encoders.Hex.DecodeData input.ScriptSig)
                                    witness
                            input, spInput)

                let maybePrivateKeys, outpoints =
                    spInputs
                    |> List.choose
                        (fun (input, spInput) ->
                            let privKey = new Key(DataEncoders.Encoders.Hex.DecodeData input.PrivateKey)
                            let outPoint = OutPoint(uint256.Parse input.TxId, input.Vout)
                            match spInput with
                            | InputForSharedSecretDerivation(_pubKey) ->
                                let isTapRoot = (Script.FromHex input.ScriptPubKey).IsScriptType ScriptType.Taproot
                                Some (Some(privKey, isTapRoot), outPoint)
                            | InputJustForSpending ->
                                Some(None, outPoint)
                            | _ -> None)
                    |> List.unzip
                        
                let privateKeys = maybePrivateKeys |> List.choose id

                match privateKeys, expectedOutput with
                | [], None -> ()
                | [], Some _ ->
                    Assert.Fail(sprintf "No inputs for shared secret derivation in test case '%s'" testCaseName)
                | _, Some expectedOutputString ->
                    let output = SilentPayments.CreateOutput privateKeys outpoints recipients.[0]
                    let outputString = output.GetEncoded() |> DataEncoders.Encoders.Hex.EncodeData
                    Assert.AreEqual(expectedOutputString, outputString, sprintf "Failure in test case '%s'" testCaseName)
                | _, None ->
                    Assert.Throws(fun () -> SilentPayments.CreateOutput privateKeys outpoints recipients.[0] |> ignore)
                    |> ignore
