namespace GWallet.Backend

open System
open System.IO
open System.Linq
open System.Net.Http

type CachedValue<'T> = ('T*DateTime)
type NotFresh<'T> =
    NotAvailable | Cached of CachedValue<'T>
type MaybeCached<'T> =
    NotFresh of NotFresh<'T> | Fresh of 'T

type PublicAddress = string
type private DietCurrency = string
type private ServerIdentifier = string

type DietCache =
    {
        UsdPrice: Map<DietCurrency,decimal>;
        Addresses: Map<PublicAddress,List<DietCurrency>>;
        Balances: Map<DietCurrency,decimal>;
    }

type ServerRanking = Map<ServerIdentifier,HistoryInfo*DateTime>

type CachedNetworkData =
    {
        UsdPrice: Map<Currency,CachedValue<decimal>>;
        Balances: Map<Currency,Map<PublicAddress,CachedValue<decimal>>>;
        OutgoingTransactions: Map<Currency,Map<PublicAddress,Map<string,CachedValue<decimal>>>>;
    }
    static member Empty =
        {
            UsdPrice = Map.empty
            Balances = Map.empty
            OutgoingTransactions = Map.empty
        }

    static member FromDietCache (dietCache: DietCache): CachedNetworkData =
        let now = DateTime.UtcNow
        let fiatPrices =
            [ for KeyValue(currencyString, price) in dietCache.UsdPrice -> (Currency.Parse currencyString,(price,now)) ]
                |> Map.ofSeq
        let balances =
            seq {
                for KeyValue(address,currencies) in dietCache.Addresses do
                    for currencyStr in currencies do
                        match dietCache.Balances.TryFind currencyStr with
                        | None -> ()
                        | Some balance ->
                            yield (Currency.Parse currencyStr),Map.empty.Add(address,(balance,now))
            } |> Map.ofSeq
        { UsdPrice = fiatPrices; Balances = balances
          OutgoingTransactions = Map.empty; }

    member self.ToDietCache(readOnlyAccounts: seq<ReadOnlyAccount>) =
        let rec extractAddressesFromAccounts (acc: Map<PublicAddress,List<DietCurrency>>) (accounts: List<IAccount>)
            : Map<PublicAddress,List<DietCurrency>> =
                match accounts with
                | [] -> acc
                | head::tail ->
                    let existingCurrenciesForHeadAddress =
                        match acc.TryFind head.PublicAddress with
                        | None -> List.Empty
                        | Some currencies -> currencies
                    let newAcc = acc.Add(head.PublicAddress, head.Currency.ToString()::existingCurrenciesForHeadAddress)
                    extractAddressesFromAccounts newAcc tail
        let fiatPrices =
            [ for (KeyValue(currency, (price,_))) in self.UsdPrice -> currency.ToString(),price ]
                |> Map.ofSeq
        let addresses = extractAddressesFromAccounts
                            Map.empty (List.ofSeq readOnlyAccounts |> List.map (fun acc -> acc:>IAccount))
        let balances =
            seq {
                for (KeyValue(currency, currencyBalances)) in self.Balances do
                    for (KeyValue(address, (balance,_))) in currencyBalances do
                        if readOnlyAccounts.Any(fun account -> (account:>IAccount).PublicAddress = address) then
                            yield (currency.ToString(),balance)
            } |> Map.ofSeq
        { UsdPrice = fiatPrices; Addresses = addresses; Balances = balances; }

type CacheFiles =
    {
        CachedNetworkData: FileInfo
        ServerStats: FileInfo
    }

module Caching =

    let private GetCacheDir() =
        let configPath = Config.GetConfigDirForThisProgram().FullName
        let configDir = DirectoryInfo(Path.Combine(configPath, "cache"))
        if not configDir.Exists then
            configDir.Create()
        configDir

    let private defaultCacheFiles =
        {
            CachedNetworkData = FileInfo(Path.Combine(GetCacheDir().FullName, "networkdata.json"))
            ServerStats = FileInfo(Path.Combine(GetCacheDir().FullName, "stats.json"))
        }

    let public ImportFromJson<'T> (cacheData: string): 'T =
        Marshalling.Deserialize cacheData

    let droppedCachedMsgWarning = "Warning: cleaning incompatible cache data found from different GWallet version"
    let private LoadFromDiskInternal<'T> (file: FileInfo): Option<'T> =
        try
            let json = File.ReadAllText file.FullName
            if String.IsNullOrWhiteSpace json then
                None
            else
                let deserializedJson = ImportFromJson json
                Some deserializedJson
        with
        | :? FileNotFoundException -> None
        | :? VersionMismatchDuringDeserializationException ->
            Console.Error.WriteLine droppedCachedMsgWarning
            None
        | :? DeserializationException ->
            // FIXME: report a warning to sentry here...
            Console.Error.WriteLine("Warning: cleaning incompatible cache data found")
            None

    let private LoadFromDisk (files: CacheFiles): bool*CachedNetworkData*ServerRanking =
        let maybeNetworkData = LoadFromDiskInternal<CachedNetworkData> files.CachedNetworkData
        let maybeFirstRun,resultingNetworkData =
            match maybeNetworkData with
            | None ->
                true,CachedNetworkData.Empty
            | Some networkData ->
                // this weird thing could happen because the previous version of GWallet didn't have a new element
                // FIXME: we should save each Map<> into its own file
                if Object.ReferenceEquals(networkData.OutgoingTransactions, null) then
                    Console.Error.WriteLine droppedCachedMsgWarning
                    true,CachedNetworkData.Empty
                else
                    false,networkData

        let maybeServerStats = LoadFromDiskInternal<ServerRanking> files.ServerStats
        match maybeServerStats with
        | None ->
            maybeFirstRun,resultingNetworkData,Map.empty
        | Some serverStats ->
            false,resultingNetworkData,serverStats

    let rec private MergeRatesInternal (oldMap: Map<'K, CachedValue<'V>>)
                                       (newMap: Map<'K, CachedValue<'V>>)
                                       (currencyList: List<'K>)
                                       (accumulator: Map<'K, CachedValue<'V>>) =
        match currencyList with
        | [] -> accumulator
        | address::tail ->
            let maybeCachedBalance = Map.tryFind address oldMap
            match maybeCachedBalance with
            | None ->
                let newCachedBalance = newMap.[address]
                let newAcc = accumulator.Add(address, newCachedBalance)
                MergeRatesInternal oldMap newMap tail newAcc
            | Some(balance,time) ->
                let newBalance,newTime = newMap.[address]
                let newAcc =
                    if (newTime > time) then
                        accumulator.Add(address, (newBalance,newTime))
                    else
                        accumulator
                MergeRatesInternal oldMap newMap tail newAcc

    let private MergeRates (oldMap: Map<'K, CachedValue<'V>>) (newMap: Map<'K, CachedValue<'V>>) =
        let currencyList = Map.toList newMap |> List.map fst
        MergeRatesInternal oldMap newMap currencyList oldMap

    let rec private MergeBalancesInternal (oldMap: Map<Currency, Map<PublicAddress,CachedValue<'V>>>)
                                          (newMap: Map<Currency, Map<PublicAddress,CachedValue<'V>>>)
                                          (addressList: List<Currency*PublicAddress>)
                                          (accumulator: Map<Currency, Map<PublicAddress,CachedValue<'V>>>)
                                              : Map<Currency, Map<PublicAddress,CachedValue<'V>>> =
        match addressList with
        | [] -> accumulator
        | (currency,address)::tail ->
            let maybeCachedBalances = Map.tryFind currency oldMap
            match maybeCachedBalances with
            | None ->
                let newCachedBalance = newMap.[currency].[address]
                let newCachedBalancesForThisCurrency = [(address,newCachedBalance)] |> Map.ofList
                let newAcc = accumulator.Add(currency, newCachedBalancesForThisCurrency)
                MergeBalancesInternal oldMap newMap tail newAcc
            | Some(balancesMapForCurrency) ->
                let accBalancesForThisCurrency = accumulator.[currency]
                let maybeCachedBalance = Map.tryFind address balancesMapForCurrency
                match maybeCachedBalance with
                | None ->
                    let newCachedBalance = newMap.[currency].[address]
                    let newAccBalances = accBalancesForThisCurrency.Add(address, newCachedBalance)
                    let newAcc = accumulator.Add(currency, newAccBalances)
                    MergeBalancesInternal oldMap newMap tail newAcc
                | Some(balance,time) ->
                    let newBalance,newTime = newMap.[currency].[address]
                    let newAcc =
                        if (newTime > time) then
                            let newAccBalances = accBalancesForThisCurrency.Add(address, (newBalance,newTime))
                            accumulator.Add(currency, newAccBalances)
                        else
                            accumulator
                    MergeBalancesInternal oldMap newMap tail newAcc

    let private MergeBalances (oldMap: Map<Currency, Map<PublicAddress,CachedValue<'V>>>)
                              (newMap: Map<Currency, Map<PublicAddress,CachedValue<'V>>>)
                                  : Map<Currency, Map<PublicAddress,CachedValue<'V>>> =
        let addressList =
            seq {
                for currency,subMap in Map.toList newMap do
                    for address,_ in Map.toList subMap do
                        yield (currency,address)
            } |> List.ofSeq
        MergeBalancesInternal oldMap newMap addressList oldMap

    // taken from http://www.fssnip.net/2z/title/All-combinations-of-list-elements
    let ListCombinations lst =
        let rec comb accLst elemLst =
            match elemLst with
            | h::t ->
                let next = [h]::List.map (fun el -> h::el) accLst @ accLst
                comb next t
            | _ -> accLst
        comb List.Empty lst

    let MapCombinations<'K,'V when 'K: comparison> (map: Map<'K,'V>): List<List<'K*'V>> =
        Map.toList map |> ListCombinations

    type MainCache(maybeCacheFiles: Option<CacheFiles>, unconfTxExpirationSpan: TimeSpan) =
        let cacheFiles =
            match maybeCacheFiles with
            | Some files -> files
            | None -> defaultCacheFiles

        let firstRun,initialSessionCachedNetworkData,initialServerStats = LoadFromDisk cacheFiles
        let mutable sessionCachedNetworkData = initialSessionCachedNetworkData
        let mutable sessionServerRanking = initialServerStats

        let SaveNetworkDataToDisk (newCachedData: CachedNetworkData) =
            let networkDataInJson = Marshalling.Serialize newCachedData

            // it is assumed that SaveToDisk is being run under a lock() block
            File.WriteAllText (cacheFiles.CachedNetworkData.FullName, networkDataInJson)

        let SaveServerRankingsToDisk (serverStats: ServerRanking) =
            let serverStatsInJson = Marshalling.Serialize serverStats

            // it is assumed that SaveToDisk is being run under a lock() block
            File.WriteAllText (cacheFiles.ServerStats.FullName, serverStatsInJson)

        let GetSumOfAllTransactions (trans: Map<Currency,Map<PublicAddress,Map<string,CachedValue<decimal>>>>)
                                    currency address: decimal =
            let now = DateTime.UtcNow
            let currencyTrans = trans.TryFind currency
            match currencyTrans with
            | None -> 0m
            | Some someMap ->
                let addressTrans = someMap.TryFind address
                match addressTrans with
                | None -> 0m
                | Some someMap ->
                    Map.toSeq someMap |>
                        Seq.sumBy (fun (txId,(txAmount,txDate)) ->
                                        // FIXME: develop some kind of cache cleanup to remove these expired txs?
                                        if (now < txDate + unconfTxExpirationSpan) then
                                            txAmount
                                        else
                                            0m
                                  )

        let rec RemoveRangeFromMap (map: Map<'K,'V>) (list: List<'K*'V>) =
            match list with
            | [] -> map
            | (key,value)::tail -> RemoveRangeFromMap (map.Remove key) tail

        let GatherDebuggingInfo (previousBalance) (currency) (address) (newCache) =
            let json1 = Marshalling.Serialize previousBalance
            let json2 = Marshalling.Serialize currency
            let json3 = Marshalling.Serialize address
            let json4 = Marshalling.Serialize newCache
            String.Join(Environment.NewLine, json1, json2, json3, json4)

        let ReportProblem (negativeBalance: decimal) (previousBalance) (currency) (address) (newCache) =
            Infrastructure.ReportError (sprintf "Negative balance '%s'. Details: %s"
                                                    (negativeBalance.ToString())
                                                    (GatherDebuggingInfo
                                                        previousBalance
                                                        currency
                                                        address
                                                        newCache))

#if DEBUG
        member this.ClearAll () =
            SaveNetworkDataToDisk CachedNetworkData.Empty
            SaveServerRankingsToDisk Map.empty
#endif

        member self.SaveSnapshot(newDietCachedData: DietCache) =
            let newCachedData = CachedNetworkData.FromDietCache newDietCachedData
            lock cacheFiles.CachedNetworkData (fun _ ->
                let newSessionCachedNetworkData =
                    let mergedBalances = MergeBalances sessionCachedNetworkData.Balances newCachedData.Balances
                    let mergedUsdPrices = MergeRates sessionCachedNetworkData.UsdPrice newCachedData.UsdPrice
                    {
                        sessionCachedNetworkData with
                            UsdPrice = mergedUsdPrices
                            Balances = mergedBalances
                    }

                sessionCachedNetworkData <- newSessionCachedNetworkData
                SaveNetworkDataToDisk newSessionCachedNetworkData
            )

        member self.GetLastCachedData (): CachedNetworkData =
            lock cacheFiles.CachedNetworkData (fun _ ->
                sessionCachedNetworkData
            )

        member self.RetreiveLastKnownUsdPrice (currency): NotFresh<decimal> =
            lock cacheFiles.CachedNetworkData (fun _ ->
                try
                    Cached(sessionCachedNetworkData.UsdPrice.Item currency)
                with
                // FIXME: rather use tryFind func instead of using a try-with block
                | :? System.Collections.Generic.KeyNotFoundException -> NotAvailable
            )

        member self.StoreLastFiatUsdPrice (currency, lastFiatUsdPrice: decimal): unit =
            lock cacheFiles.CachedNetworkData (fun _ ->
                let time = DateTime.UtcNow

                let newCachedValue =
                    { sessionCachedNetworkData
                        with UsdPrice = sessionCachedNetworkData.UsdPrice.Add(currency, (lastFiatUsdPrice, time)) }
                sessionCachedNetworkData <- newCachedValue

                SaveNetworkDataToDisk newCachedValue
            )

        member self.RetreiveLastCompoundBalance (address: PublicAddress) (currency: Currency): NotFresh<decimal> =
            lock cacheFiles.CachedNetworkData (fun _ ->
                let balance =
                    try
                        Cached((sessionCachedNetworkData.Balances.Item currency).Item address)
                    with
                    // FIXME: rather use tryFind func instead of using a try-with block
                    | :? System.Collections.Generic.KeyNotFoundException -> NotAvailable
                match balance with
                | NotAvailable ->
                    NotAvailable
                | Cached(balance,time) ->
                    let allTransSum =
                        GetSumOfAllTransactions sessionCachedNetworkData.OutgoingTransactions currency address
                    let compoundBalance = balance - allTransSum
                    if (compoundBalance < 0.0m) then
                        ReportProblem compoundBalance
                                      None
                                      currency
                                      address
                                      sessionCachedNetworkData
                        Cached(0.0m,time)
                    else
                        Cached(compoundBalance,time)
            )

        member self.TryRetreiveLastCompoundBalance (address: PublicAddress) (currency: Currency): Option<decimal> =
            let maybeCachedBalance = self.RetreiveLastCompoundBalance address currency
            match maybeCachedBalance with
            | NotAvailable ->
                None
            | Cached(cachedBalance,_) ->
                Some cachedBalance

        member self.RetreiveAndUpdateLastCompoundBalance (address: PublicAddress)
                                                         (currency: Currency)
                                                         (newBalance: decimal)
                                                             : CachedValue<decimal> =
            let time = DateTime.UtcNow
            lock cacheFiles.CachedNetworkData (fun _ ->
                let newCachedValueWithNewBalance,previousBalance =
                    let newCurrencyBalances,previousBalance =
                        match sessionCachedNetworkData.Balances.TryFind currency with
                        | None ->
                            Map.empty,None
                        | Some currencyBalances ->
                            let maybePreviousBalance = currencyBalances.TryFind address
                            currencyBalances,maybePreviousBalance
                    {
                        sessionCachedNetworkData with
                            Balances = sessionCachedNetworkData.Balances.Add(currency,
                                                                             newCurrencyBalances.Add(address,
                                                                                                     (newBalance, time)))
                    },previousBalance

                let newCachedValueWithNewBalanceAndMaybeLessTransactions =
                    match previousBalance with
                    | None ->
                        newCachedValueWithNewBalance
                    | Some (previousCachedBalance,_) ->
                        if newBalance <> previousCachedBalance && previousCachedBalance > newBalance then
                            match newCachedValueWithNewBalance.OutgoingTransactions.TryFind currency with
                            | None ->
                                newCachedValueWithNewBalance
                            | Some currencyAddresses ->
                                match currencyAddresses.TryFind address with
                                | None ->
                                    newCachedValueWithNewBalance
                                | Some addressTransactions ->
                                    let allCombinationsOfTransactions = MapCombinations addressTransactions
                                    let newAddressTransactions =
                                        match List.tryFind (fun combination ->
                                                               let txSumAmount = List.sumBy (fun (txId,(txAmount,_)) ->
                                                                                                 txAmount) combination
                                                               previousCachedBalance - txSumAmount = newBalance
                                                           ) allCombinationsOfTransactions with
                                        | None ->
                                            addressTransactions
                                        | Some combination ->
                                            RemoveRangeFromMap addressTransactions combination
                                    let newOutgoingTransactions =
                                        newCachedValueWithNewBalance
                                            .OutgoingTransactions.Add(currency,
                                                                      currencyAddresses.Add(address,
                                                                                            newAddressTransactions))
                                    {
                                        newCachedValueWithNewBalance with
                                            OutgoingTransactions = newOutgoingTransactions
                                    }
                        else
                            newCachedValueWithNewBalance

                sessionCachedNetworkData <- newCachedValueWithNewBalanceAndMaybeLessTransactions

                SaveNetworkDataToDisk newCachedValueWithNewBalanceAndMaybeLessTransactions

                let allTransSum =
                    GetSumOfAllTransactions newCachedValueWithNewBalanceAndMaybeLessTransactions.OutgoingTransactions
                                            currency
                                            address
                let compoundBalance = newBalance - allTransSum
                if (compoundBalance < 0.0m) then
                    ReportProblem compoundBalance
                                  previousBalance
                                  currency
                                  address
                                  newCachedValueWithNewBalanceAndMaybeLessTransactions
                    0.0m,time
                else
                    compoundBalance,time
            )

        member private self.StoreTransactionRecord (address: PublicAddress)
                                                   (currency: Currency)
                                                   (txId: string)
                                                   (amount: decimal)
                                                       : unit =
            let time = DateTime.UtcNow
            lock cacheFiles.CachedNetworkData (fun _ ->
                let newCurrencyAddresses =
                    match sessionCachedNetworkData.OutgoingTransactions.TryFind currency with
                    | None ->
                        Map.empty
                    | Some currencyAddresses ->
                        currencyAddresses
                let newAddressTransactions =
                    match newCurrencyAddresses.TryFind address with
                    | None ->
                        Map.empty.Add(txId, (amount, time))
                    | Some addressTransactions ->
                        addressTransactions.Add(txId, (amount, time))

                let newOutgoingTxs =
                    sessionCachedNetworkData.OutgoingTransactions.Add(currency,
                                                                      newCurrencyAddresses.Add(address,
                                                                                               newAddressTransactions))
                let newCachedValue = { sessionCachedNetworkData with OutgoingTransactions = newOutgoingTxs }

                sessionCachedNetworkData <- newCachedValue

                SaveNetworkDataToDisk newCachedValue
            )

        member self.StoreOutgoingTransaction (address: PublicAddress)
                                             (transactionCurrency: Currency)
                                             (feeCurrency: Currency)
                                             (txId: string)
                                             (amount: decimal)
                                             (feeAmount: decimal)
                                                 : unit =

            self.StoreTransactionRecord address transactionCurrency txId amount
            if transactionCurrency <> feeCurrency && (not Config.EthTokenEstimationCouldBeBuggyAsInNotAccurate) then
                self.StoreTransactionRecord address feeCurrency txId feeAmount

        member self.SaveServerLastStat (server, historyInfo): unit =
            lock cacheFiles.ServerStats (fun _ ->
                let newCachedValue =
                        sessionServerRanking.Add(server, (historyInfo, DateTime.UtcNow))

                sessionServerRanking <- newCachedValue

                SaveServerRankingsToDisk newCachedValue
            )

        member self.RetreiveLastServerHistory (serverId: string): Option<HistoryInfo> =
            lock cacheFiles.ServerStats (fun _ ->
                match sessionServerRanking.TryFind serverId with
                | None ->
                    if Config.DebugLog then
                        Console.Error.WriteLine (sprintf "WARNING: no history stats about %s" serverId)
                    None
                | Some (historyInfo,_) -> Some historyInfo
            )

        member self.BootstrapServerStatsFromTrustedSource(): Async<unit> =
            let downloadFile url: Async<Option<string>> =
                let tryDownloadFile url: Async<string> =
                    async {
                        use httpClient = new HttpClient()
                        let uri = Uri url
                        let! response = Async.AwaitTask (httpClient.GetAsync uri)
                        let isSuccess = response.IsSuccessStatusCode
                        let! content = Async.AwaitTask <| response.Content.ReadAsStringAsync()
                        if isSuccess then
                            return content
                        else
                            Console.Error.WriteLine ("WARNING: error trying to retreive server stats: " + content)
                            return failwith content
                    }
                async {
                    try
                        let! content = tryDownloadFile url
                        return Some content
                    with
                    // should we specify HttpRequestException?
                    | _ ->
                        return None
                }

            let targetBranch = "stable"
            let username = "knocte"
            let projName1,projName2 = "geewallet","gwallet"
            let githubBaseUrl,gitlabBaseUrl = "https://raw.githubusercontent.com","https://gitlab.com"
            let pathToFile = "src/GWallet.Backend/lastServerStats.json"

            let knocteGithub =
                sprintf "%s/%s/%s/%s/%s"
                        githubBaseUrl username projName2 targetBranch pathToFile

            let knocteGitLab =
                sprintf "%s/%s/%s/raw/%s/%s"
                        gitlabBaseUrl username projName1 targetBranch pathToFile

            let allUrls = [ knocteGithub; knocteGitLab ]
            let allJobs =
                allUrls |> Seq.map downloadFile

            async {
                let! maybeLastServerStatsInJson = FSharpUtil.AsyncExtensions.Choice allJobs
                match maybeLastServerStatsInJson with
                | None ->
                    Console.Error.WriteLine "WARNING: Couldn't reach a trusted server to retreive server stats to bootstrap cache, running in offline mode?"
                | Some lastServerStatsInJson ->
                    let lastServerStats = ImportFromJson<ServerRanking> lastServerStatsInJson
                    lock cacheFiles.ServerStats (fun _ ->
                        sessionServerRanking <- lastServerStats
                        SaveServerRankingsToDisk lastServerStats
                    )
            }

        member __.FirstRun
            with get() = firstRun

    let Instance = MainCache (None, TimeSpan.FromDays 1.0)
