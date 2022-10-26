namespace GWallet.Backend.UtxoCoin

open System
open System.Net

open GWallet.Backend
open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks


module BtcTransactionPrinting =
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

    let PrintTransactions (maxTransactionsCount: uint32) (btcAddress: string) =
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
                            return! processHistory rest (maxTransactionsToPrint - 1u)
                        else
                            return! processHistory rest maxTransactionsToPrint
                    }
                | _ -> async { return () }
            
            do! processHistory sortedHistory maxTransactionsCount
            Console.WriteLine("End of results")
            Console.ReadKey() |> ignore
            return 0
        }
        |> Async.RunSynchronously
