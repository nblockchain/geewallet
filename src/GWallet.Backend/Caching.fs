namespace GWallet.Backend

open System
open System.IO
open System.Linq

type CachedValue<'T> = ('T*DateTime)
type NotFresh<'T> =
    NotAvailable | Cached of CachedValue<'T>
type MaybeCached<'T> =
    NotFresh of NotFresh<'T> | Fresh of 'T

type PublicAddress = string
type private DietCurrency = string

type DietCache =
    {
        UsdPrice: Map<DietCurrency,decimal>;
        Addresses: Map<PublicAddress,List<DietCurrency>>;
        Balances: Map<DietCurrency,decimal>;
    }

type CachedNetworkData =
    {
        UsdPrice: Map<Currency,CachedValue<decimal>>;
        Balances: Map<Currency,Map<PublicAddress,CachedValue<decimal>>>;
        OutgoingTransactions: Map<Currency,Map<PublicAddress,Map<string,CachedValue<decimal>>>>;
    }
    static member Empty = { UsdPrice = Map.empty; Balances = Map.empty; OutgoingTransactions = Map.empty; }

    static member FromDietCache (dietCache: DietCache): CachedNetworkData =
        let now = DateTime.Now
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
        { UsdPrice = fiatPrices; Balances = balances; OutgoingTransactions = Map.empty }

    member this.ToDietCache(readOnlyAccounts: seq<ReadOnlyAccount>) =
        let rec extractAddressesFromAccounts (acc: Map<PublicAddress,List<DietCurrency>>) (accounts: List<IAccount>)
            : Map<PublicAddress,List<DietCurrency>> =
                match accounts with
                | [] -> acc
                | head::tail ->
                    let existingCurrenciesForHeadAddress =
                        match acc.TryFind head.PublicAddress with
                        | None -> []
                        | Some currencies -> currencies
                    let newAcc = acc.Add(head.PublicAddress, head.Currency.ToString()::existingCurrenciesForHeadAddress)
                    extractAddressesFromAccounts newAcc tail
        let fiatPrices =
            [ for (KeyValue(currency, (price,_))) in this.UsdPrice -> currency.ToString(),price ]
                |> Map.ofSeq
        let addresses = extractAddressesFromAccounts
                            Map.empty (List.ofSeq readOnlyAccounts |> List.map (fun acc -> acc:>IAccount))
        let balances =
            seq {
                for (KeyValue(currency, currencyBalances)) in this.Balances do
                    for (KeyValue(address, (balance,_))) in currencyBalances do
                        if readOnlyAccounts.Any(fun account -> (account:>IAccount).PublicAddress = address) then
                            yield (currency.ToString(),balance)
            } |> Map.ofSeq
        { UsdPrice = fiatPrices; Addresses = addresses; Balances = balances; }

module Caching =

    let private GetCacheDir() =
        let configPath = Config.GetConfigDirForThisProgram().FullName
        let configDir = DirectoryInfo(Path.Combine(configPath, "cache"))
        if not configDir.Exists then
            configDir.Create()
        configDir

    let private defaultCacheFile = FileInfo(Path.Combine(GetCacheDir().FullName, "last.json"))

    let public ImportFromJson (cacheData: string): CachedNetworkData =
        Marshalling.Deserialize cacheData

    let droppedCachedMsgWarning = "Warning: cleaning incompatible cache data found from different GWallet version"
    let private LoadFromDiskInternal (file: FileInfo): Option<CachedNetworkData> =
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

    let private LoadFromDisk (file: FileInfo): Option<CachedNetworkData> =
        let maybeNetworkData = LoadFromDiskInternal file
        match maybeNetworkData with
        | None ->
            None
        | Some networkData ->
            // this weird thing could happen because the previous version of GWallet didn't have an OutgoingTrans element
            if (Object.ReferenceEquals(networkData.OutgoingTransactions, null)) then
                Console.Error.WriteLine droppedCachedMsgWarning
                None
            else
                maybeNetworkData

    let rec private MergeRatesInternal (oldMap: Map<'K, CachedValue<'V>>)
                                       (newMap: Map<'K, CachedValue<'V>>)
                                       (currencyList: list<'K>)
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
                                          (addressList: list<Currency*PublicAddress>)
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
        comb [] lst

    let MapCombinations<'K,'V when 'K: comparison> (map: Map<'K,'V>): List<List<'K*'V>> =
        Map.toList map |> ListCombinations

    type MainCache(maybeCacheFile: Option<FileInfo>, unconfTxExpirationSpan: TimeSpan) =
        let cacheFile =
            match maybeCacheFile with
            | Some file -> file
            | None -> defaultCacheFile

        let mutable sessionCachedNetworkData: Option<CachedNetworkData> = LoadFromDisk cacheFile
        let lockObject = Object()

        let SaveToDisk (newCachedData: CachedNetworkData) =
            let json = Marshalling.Serialize newCachedData
            File.WriteAllText (cacheFile.FullName, json)

        let GetSumOfAllTransactions (trans: Map<Currency,Map<PublicAddress,Map<string,CachedValue<decimal>>>>)
                                    currency address: decimal =
            let now = DateTime.Now
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


#if DEBUG
        member this.ClearAll () =
            SaveToDisk CachedNetworkData.Empty
#endif

        member self.SaveSnapshot(newDietCachedData: DietCache) =
            let newCachedData = CachedNetworkData.FromDietCache newDietCachedData
            lock lockObject (fun _ ->
                let newSessionCachedNetworkData =
                    match sessionCachedNetworkData with
                    | None ->
                        newCachedData
                    | Some networkData ->
                        let mergedBalances = MergeBalances networkData.Balances newCachedData.Balances
                        let mergedUsdPrices = MergeRates networkData.UsdPrice newCachedData.UsdPrice
                        {
                            UsdPrice = mergedUsdPrices;
                            Balances = mergedBalances;
                            OutgoingTransactions = Map.empty;
                        }

                sessionCachedNetworkData <- Some(newSessionCachedNetworkData)
                SaveToDisk newSessionCachedNetworkData
            )

        member self.GetLastCachedData (): CachedNetworkData =
            lock lockObject (fun _ ->
                sessionCachedNetworkData.Value
            )

        member self.RetreiveLastKnownUsdPrice (currency): NotFresh<decimal> =
            lock lockObject (fun _ ->
                match sessionCachedNetworkData with
                | None -> NotAvailable
                | Some(networkData) ->
                    try
                        Cached(networkData.UsdPrice.Item currency)
                    with
                    // FIXME: rather use tryFind func instead of using a try-with block
                    | :? System.Collections.Generic.KeyNotFoundException -> NotAvailable
            )

        member self.StoreLastFiatUsdPrice (currency, lastFiatUsdPrice: decimal): unit =
            lock lockObject (fun _ ->
                let time = DateTime.Now

                let newCachedValue =
                    match sessionCachedNetworkData with
                    | None ->
                        {
                            UsdPrice = Map.empty.Add(currency, (lastFiatUsdPrice, time));
                            Balances = Map.empty;
                            OutgoingTransactions = Map.empty;
                        }
                    | Some previousCachedData ->
                        {
                            UsdPrice = previousCachedData.UsdPrice.Add(currency, (lastFiatUsdPrice, time));
                            Balances = previousCachedData.Balances;
                            OutgoingTransactions = previousCachedData.OutgoingTransactions;
                        }
                sessionCachedNetworkData <- Some newCachedValue

                SaveToDisk newCachedValue
            )
            ()

        member self.RetreiveLastCompoundBalance (address: PublicAddress) (currency: Currency): NotFresh<decimal> =
            lock lockObject (fun _ ->
                match sessionCachedNetworkData with
                | None -> NotAvailable
                | Some networkData ->
                    let balance =
                        try
                            Cached((networkData.Balances.Item currency).Item address)
                        with
                        // FIXME: rather use tryFind func instead of using a try-with block
                        | :? System.Collections.Generic.KeyNotFoundException -> NotAvailable
                    match balance with
                    | NotAvailable ->
                        NotAvailable
                    | Cached(balance,time) ->
                        let allTransSum = GetSumOfAllTransactions networkData.OutgoingTransactions currency address
                        let compoundBalance = balance - allTransSum
                        Cached(compoundBalance,time)
            )

        member self.RetreiveAndUpdateLastCompoundBalance (address: PublicAddress)
                                                         (currency: Currency)
                                                         (newBalance: decimal)
                                                             : CachedValue<decimal> =
            let time = DateTime.Now
            lock lockObject (fun _ ->
                let newCachedValueWithNewBalance,previousBalance =
                    match sessionCachedNetworkData with
                    | None ->
                        {
                            UsdPrice = Map.empty;
                            Balances = Map.empty.Add(currency, Map.empty.Add(address, (newBalance, time)));
                            OutgoingTransactions = Map.empty;
                        },None
                    | Some previousCachedData ->
                        let newCurrencyBalances,previousBalance =
                            match previousCachedData.Balances.TryFind currency with
                            | None ->
                                Map.empty,None
                            | Some currencyBalances ->
                                let maybePreviousBalance = currencyBalances.TryFind address
                                currencyBalances,maybePreviousBalance
                        {
                            UsdPrice = previousCachedData.UsdPrice;
                            Balances = previousCachedData.Balances.Add(currency,
                                                                       newCurrencyBalances.Add(address,
                                                                                              (newBalance, time)));
                            OutgoingTransactions = previousCachedData.OutgoingTransactions;
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
                                    let newAdressTransactionsTransactions =
                                        match List.tryFind (fun combination ->
                                                               let txSumAmount = List.sumBy (fun (key,(txAmount,_)) ->
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
                                                                                            newAdressTransactionsTransactions))
                                    {
                                        UsdPrice = newCachedValueWithNewBalance.UsdPrice;
                                        Balances = newCachedValueWithNewBalance.Balances;
                                        OutgoingTransactions = newOutgoingTransactions;
                                    }
                        else
                            newCachedValueWithNewBalance

                sessionCachedNetworkData <- Some newCachedValueWithNewBalanceAndMaybeLessTransactions

                SaveToDisk newCachedValueWithNewBalanceAndMaybeLessTransactions

                let allTransSum =
                    GetSumOfAllTransactions newCachedValueWithNewBalanceAndMaybeLessTransactions.OutgoingTransactions
                                            currency
                                            address
                let compoundBalance = newBalance - allTransSum
                compoundBalance,time
            )

        member private self.StoreTransactionRecord (address: PublicAddress)
                                                   (currency: Currency)
                                                   (txId: string)
                                                   (amount: decimal)
                                                       : unit =
            let time = DateTime.Now
            lock lockObject (fun _ ->
                let newCachedValue =
                    match sessionCachedNetworkData with
                    | None ->
                        {
                            UsdPrice = Map.empty;
                            Balances = Map.empty;
                            OutgoingTransactions = Map.empty.Add(currency,
                                                                 Map.empty.Add(address,
                                                                               Map.empty.Add(txId,
                                                                                             (amount, time))));
                        }
                    | Some previousCachedData ->
                        let newCurrencyAddresses =
                            match previousCachedData.OutgoingTransactions.TryFind currency with
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

                        {
                            UsdPrice = previousCachedData.UsdPrice;
                            Balances = previousCachedData.Balances;
                            OutgoingTransactions =
                                previousCachedData.OutgoingTransactions.Add(currency,
                                                                            newCurrencyAddresses.Add(address,
                                                                                                     newAddressTransactions));
                        }

                sessionCachedNetworkData <- Some newCachedValue

                SaveToDisk newCachedValue
            )

        member self.StoreOutgoingTransaction (address: PublicAddress)
                                             (transactionCurrency: Currency)
                                             (feeCurrency: Currency)
                                             (txId: string)
                                             (transactionAmount: decimal)
                                             (feeAmount: decimal)
                                                 : unit =
            if (transactionCurrency = feeCurrency) then
                self.StoreTransactionRecord address transactionCurrency txId (transactionAmount + feeAmount)
            else
                self.StoreTransactionRecord address transactionCurrency txId transactionAmount
                self.StoreTransactionRecord address feeCurrency txId feeAmount

    let Instance = MainCache (None, TimeSpan.FromDays 1.0)
