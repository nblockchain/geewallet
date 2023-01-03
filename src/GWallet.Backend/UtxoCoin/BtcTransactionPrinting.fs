namespace GWallet.Backend.UtxoCoin

open System
open System.Net

open GWallet.Backend
open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks


module BtcTransactionPrinting =
    type private ProcessedHistoryEntry =
        {
            Date: DateTime
            Outputs: NBitcoin.TxOutList 
        }

    type private EntryWithSharedAddress =
        {
            Date: DateTime
            SharedOutputs: seq<NBitcoin.TxOut>
        }

    let GetBitcoinPriceForDate (date: DateTime) : Async<Result<decimal, Exception>> =
        async {
            try
                let baseUrl = 
                    let dateFormated = date.ToString("dd-MM-yyyy")
                    SPrintF1 "https://api.coingecko.com/api/v3/coins/bitcoin/history?date=%s&localization=false" dateFormated
                let uri = Uri baseUrl
                use webClient = new WebClient()
                let task = webClient.DownloadStringTaskAsync uri
                let! result = Async.AwaitTask task
                let json = Newtonsoft.Json.Linq.JObject.Parse result
                return Ok(json.["market_data"].["current_price"].["usd"].ToObject<decimal>())
            with
            | ex ->
                return Error ex
        }

    let PrintTransactions (maxTransactionsCountOption: uint32 option) (btcAddress: string) =
        let maxTransactionsCount = defaultArg maxTransactionsCountOption UInt32.MaxValue
        let address = NBitcoin.BitcoinAddress.Create(btcAddress, NBitcoin.Network.Main)
        async { 
            let scriptHash = Account.GetElectrumScriptHashFromPublicAddress Currency.BTC btcAddress
            let! history =
                Server.Query
                    Currency.BTC
                    (QuerySettings.Default ServerSelectionMode.Fast)
                    (ElectrumClient.GetBlockchainScriptHashHistory scriptHash)
                    None

            let sortedHistory = history |> List.sortByDescending (fun entry -> entry.Height)
            
            let rec processHistory history (maxTransactionsToPrint: uint32) =
                match history with
                | nextEntry :: rest when maxTransactionsToPrint > 0u ->
                    async {
                        let! transaction = 
                            Server.Query
                                Currency.BTC
                                (QuerySettings.Default ServerSelectionMode.Fast)
                                (ElectrumClient.GetBlockchainTransactionVerbose nextEntry.TxHash)
                                None
                        let transactionInfo = NBitcoin.Transaction.Parse(transaction.Hex, NBitcoin.Network.Main)

                        let incomingOutputs =
                            transactionInfo.Outputs 
                            |> Seq.filter (fun output -> output.IsTo address)

                        if not (Seq.isEmpty incomingOutputs) then
                            let amount = incomingOutputs |> Seq.sumBy (fun txOut -> txOut.Value)
                            let dateTime = 
                                let startOfUnixEpoch = DateTime(1970, 1, 1)
                                startOfUnixEpoch + TimeSpan.FromSeconds(float transaction.Time)
                            let! bitcoinPrice = GetBitcoinPriceForDate dateTime
                            Console.WriteLine(SPrintF1 "BTC amount: %A" amount)
                            match bitcoinPrice with
                            | Ok price -> 
                                let bitcoinAmount = amount.ToDecimal(NBitcoin.MoneyUnit.BTC) 
                                Console.WriteLine(SPrintF1 "~USD amount: %s" ((bitcoinAmount * price).ToString("F2")))
                            | Error exn -> 
                                Console.WriteLine("Could not get bitcoin price for the date. An error has occured:\n" + exn.ToString())
                            Console.WriteLine(SPrintF1 "date: %A UTC" dateTime)
                            Console.WriteLine()
                            let! otherEntries = processHistory rest (maxTransactionsToPrint - 1u)
                            return { Date = dateTime; Outputs = transactionInfo.Outputs } :: otherEntries
                        else
                            return! processHistory rest maxTransactionsToPrint
                    }
                | _ -> async { return [] }
            
            let! processedEntries = processHistory sortedHistory maxTransactionsCount

            if maxTransactionsCountOption.IsSome then
                let getAddress (output: NBitcoin.TxOut) = 
                    output.ScriptPubKey.GetDestinationAddress NBitcoin.Network.Main

                let allAddressesExceptOurs = 
                    processedEntries 
                    |> Seq.collect(
                        fun entry -> 
                            entry.Outputs 
                            |> Seq.map getAddress)
                    |> Seq.filter (fun each -> each <> address)
                    |> Seq.distinct
                    |> Seq.cache

                let sharedAddresses =
                    allAddressesExceptOurs
                    |> Seq.filter (
                        fun addr ->
                            processedEntries
                            |> Seq.forall(
                                fun entry -> 
                                    entry.Outputs 
                                    |> Seq.exists (fun output -> output.IsTo addr) ) )
                    |> Seq.cache

                let entriesWithSharedAddresses =
                    processedEntries
                    |> Seq.choose (
                        fun entry -> 
                            let sharedOutputs =
                                entry.Outputs 
                                |> Seq.filter (
                                    fun output -> 
                                        sharedAddresses |> Seq.exists (fun addr -> output.IsTo addr))
                            if sharedOutputs |> Seq.isEmpty then 
                                None 
                            else 
                                let outputsToOurAddress = entry.Outputs |> Seq.filter (fun output -> output.IsTo address)
                                Some { 
                                    SharedOutputs = sharedOutputs |> Seq.append outputsToOurAddress
                                    Date = entry.Date 
                                } )
                    |> Seq.cache

                if (entriesWithSharedAddresses |> Seq.length) >= 2 then
                    Console.WriteLine(SPrintF1 "Transactions with outputs shared with %A:\n" address)
                    for entry in entriesWithSharedAddresses do
                        let totalAmount = 
                            entry.SharedOutputs 
                            |> Seq.sumBy (fun txOut -> txOut.Value)
                        let sharedOutputs = 
                            entry.SharedOutputs 
                            |> Seq.groupBy getAddress
                        let currentSharedAddresses = 
                            sharedOutputs 
                            |> Seq.map fst

                        Console.WriteLine(SPrintF1 "Transaction with outputs to %s" (String.Join(", ", currentSharedAddresses)))
                        Console.WriteLine(SPrintF1 "Date: %A UTC" entry.Date)
                        Console.WriteLine(SPrintF1 "Total BTC: %A" totalAmount)
                        for addr, outputs in sharedOutputs do
                            let amount = outputs |> Seq.sumBy (fun txOut -> txOut.Value)
                            let percentage = 
                                amount.ToDecimal(NBitcoin.MoneyUnit.BTC) / totalAmount.ToDecimal(NBitcoin.MoneyUnit.BTC) * 100.0m
                            Console.WriteLine(SPrintF3 "Sent %A BTC to %A (%s%%)" amount addr (percentage.ToString("F2")))
                        Console.WriteLine()

            Console.WriteLine("End of results")
            Console.ReadKey() |> ignore
            return 0
        }
        |> Async.RunSynchronously
