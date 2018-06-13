namespace GWallet.Backend

open System
open System.IO
open System.Collections.Generic

type Cached<'T> = ('T*DateTime)
type NotFresh<'T> =
    NotAvailable | Cached of Cached<'T>
type MaybeCached<'T> =
    NotFresh of NotFresh<'T> | Fresh of 'T
type PublicAddress = string

type CachedNetworkData =
    {
        UsdPrice: Map<Currency,Cached<decimal>>;
        Balances: Map<Currency,Map<PublicAddress,Cached<decimal>>>
    }

module Caching =

    let private GetCacheDir() =
        let configPath = Config.GetConfigDirForThisProgram().FullName
        let configDir = DirectoryInfo(Path.Combine(configPath, "cache"))
        if not configDir.Exists then
            configDir.Create()
        configDir

    let private lastCacheFile = Path.Combine(GetCacheDir().FullName, "last.json")

    let public ImportFromJson (cacheData: string): CachedNetworkData =
        Marshalling.Deserialize cacheData

    let private LoadFromDisk (): Option<CachedNetworkData> =
        try
            let json = File.ReadAllText(lastCacheFile)
            let deserializedJson = ImportFromJson(json)
            Some(deserializedJson)
        with
        | :? FileNotFoundException -> None
        | :? VersionMismatchDuringDeserializationException ->
            Console.Error.WriteLine("Warning: cleaning incompatible cache data found from different GWallet version")
            None
        | :? DeserializationException ->
            // FIXME: report a warning to sentry here...
            Console.Error.WriteLine("Warning: cleaning incompatible cache data found")
            None

    let mutable private sessionCachedNetworkData: Option<CachedNetworkData> = LoadFromDisk ()
    let private lockObject = Object()

    let internal GetLastCachedData (): CachedNetworkData =
        lock lockObject (fun _ ->
            sessionCachedNetworkData.Value
        )

    let public ExportToJson (newCachedData: CachedNetworkData): string =
        Marshalling.Serialize(newCachedData)

    let private SaveToDisk (newCachedData: CachedNetworkData) =
        let json = ExportToJson (newCachedData)
        File.WriteAllText(lastCacheFile, json)

    let rec private MergeRatesInternal (oldMap: Map<'K, Cached<'V>>)
                                  (newMap: Map<'K, Cached<'V>>)
                                  (currencyList: list<'K>)
                                  (accumulator: Map<'K, Cached<'V>>) =
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

    let private MergeRates (oldMap: Map<'K, Cached<'V>>) (newMap: Map<'K, Cached<'V>>) =
        let currencyList = Map.toList newMap |> List.map fst
        MergeRatesInternal oldMap newMap currencyList oldMap

    let rec private MergeBalancesInternal (oldMap: Map<Currency, Map<PublicAddress,Cached<'V>>>)
                                  (newMap: Map<Currency, Map<PublicAddress,Cached<'V>>>)
                                  (addressList: list<Currency*PublicAddress>)
                                  (accumulator: Map<Currency, Map<PublicAddress,Cached<'V>>>)
                                      : Map<Currency, Map<PublicAddress,Cached<'V>>> =
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

    let private MergeBalances (oldMap: Map<Currency, Map<PublicAddress,Cached<'V>>>)
                      (newMap: Map<Currency, Map<PublicAddress,Cached<'V>>>)
                          : Map<Currency, Map<PublicAddress,Cached<'V>>> =
        let addressList =
            seq {
                for currency,subMap in Map.toList newMap do
                    for address,_ in Map.toList subMap do
                        yield (currency,address)
            } |> List.ofSeq
        MergeBalancesInternal oldMap newMap addressList oldMap

    let public SaveSnapshot(newCachedData: CachedNetworkData) =
        lock lockObject (fun _ ->
            let newSessionCachedNetworkData =
                match sessionCachedNetworkData with
                | None ->
                    newCachedData
                | Some(networkData) ->
                    let mergedBalances = MergeBalances networkData.Balances newCachedData.Balances
                    let mergedUsdPrices = MergeRates networkData.UsdPrice newCachedData.UsdPrice
                    { UsdPrice = mergedUsdPrices; Balances = mergedBalances}

            sessionCachedNetworkData <- Some(newSessionCachedNetworkData)
            SaveToDisk newSessionCachedNetworkData
        )

    let internal RetreiveLastKnownUsdPrice (currency): NotFresh<decimal> =
        lock lockObject (fun _ ->
            match sessionCachedNetworkData with
            | None -> NotAvailable
            | Some(networkData) ->
                try
                    Cached(networkData.UsdPrice.Item currency)
                with
                | :? KeyNotFoundException -> NotAvailable
        )

    let internal RetreiveLastBalance (address: PublicAddress, currency: Currency): NotFresh<decimal> =
        lock lockObject (fun _ ->
            match sessionCachedNetworkData with
            | None -> NotAvailable
            | Some(networkData) ->
                try
                    Cached((networkData.Balances.Item currency).Item address)
                with
                | :? KeyNotFoundException -> NotAvailable
        )

    let internal StoreLastFiatUsdPrice (currency, lastFiatUsdPrice: decimal): unit =
        lock lockObject (fun _ ->
            let time = DateTime.Now

            let newCachedValue =
                match sessionCachedNetworkData with
                | None ->
                    { UsdPrice = Map.empty.Add(currency, (lastFiatUsdPrice, time));
                      Balances = Map.empty }
                | Some(previousCachedData) ->
                    { UsdPrice = previousCachedData.UsdPrice.Add(currency, (lastFiatUsdPrice, time));
                      Balances = previousCachedData.Balances }
            sessionCachedNetworkData <- Some(newCachedValue)

            SaveToDisk newCachedValue
        )
        ()

    let internal StoreLastBalance (address: PublicAddress, currency: Currency) (lastBalance: decimal): unit =
        lock lockObject (fun _ ->
            let time = DateTime.Now

            let newCachedValue =
                match sessionCachedNetworkData with
                | None ->
                    { UsdPrice = Map.empty;
                      Balances = Map.empty.Add(currency, Map.empty.Add(address, (lastBalance, time)))}
                | Some(previousCachedData) ->
                    let newCurrencyBalances =
                        match previousCachedData.Balances.TryFind currency with
                        | None ->
                            Map.empty
                        | Some currencyBalances ->
                            currencyBalances
                    { UsdPrice = previousCachedData.UsdPrice;
                      Balances = previousCachedData.Balances.Add(currency,
                                                                 newCurrencyBalances.Add(address, (lastBalance,time))) }
            sessionCachedNetworkData <- Some(newCachedValue)

            SaveToDisk newCachedValue
        )
        ()

