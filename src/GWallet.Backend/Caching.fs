namespace GWallet.Backend

open System
open System.IO
open System.Collections.Generic

open Newtonsoft.Json

type Cached<'T> = ('T*DateTime)
type NotFresh<'T> =
    NotAvailable | Cached of Cached<'T>
type MaybeCached<'T> =
    NotFresh of NotFresh<'T> | Fresh of 'T

type CachedNetworkData =
    {
        UsdPrice: Map<Currency,Cached<decimal>>;
        Balances: Map<string,Cached<decimal>>
    }

module Caching =

    let private GetCacheDir() =
        let configPath = Config.GetConfigDirForThisProgram().FullName
        let configDir = DirectoryInfo(Path.Combine(configPath, "cache"))
        if not configDir.Exists then
            configDir.Create()
        configDir

    let private lastCacheFile = Path.Combine(GetCacheDir().FullName, "last.json")

    let public ImportFromJson (cacheData: string) =
        JsonConvert.DeserializeObject<CachedNetworkData>(cacheData)

    let private LoadFromDisk (): Option<CachedNetworkData> =
        try
            let json = File.ReadAllText(lastCacheFile)
            let deserializedJson = ImportFromJson(json)
            Some(deserializedJson)
        with
        | :? FileNotFoundException -> None

    let mutable private sessionCachedNetworkData: Option<CachedNetworkData> = LoadFromDisk ()
    let private lockObject = Object()

    let internal GetLastCachedData (): CachedNetworkData =
        lock lockObject (fun _ ->
            sessionCachedNetworkData.Value
        )

    let public ExportToJson (newCachedData: Option<CachedNetworkData>): string =
        JsonConvert.SerializeObject(newCachedData,
                                    FSharpUtil.CustomIdiomaticDuConverter())

    let private SaveToDisk (newCachedData: Option<CachedNetworkData>) =
        let json = ExportToJson (newCachedData)
        File.WriteAllText(lastCacheFile, json)

    let rec private MergeInternal (oldMap: Map<'K, Cached<'V>>)
                                  (newMap: Map<'K, Cached<'V>>)
                                  (addressList: list<'K>)
                                  (accumulator: Map<'K, Cached<'V>>) =
        match addressList with
        | [] -> accumulator
        | address::tail ->
            let maybeCachedBalance = Map.tryFind address oldMap
            match maybeCachedBalance with
            | None ->
                let newCachedBalance = newMap.[address]
                let newAcc = accumulator.Add(address, newCachedBalance)
                MergeInternal oldMap newMap tail newAcc
            | Some(balance,time) ->
                let newBalance,newTime = newMap.[address]
                let newAcc =
                    if (newTime > time) then
                        accumulator.Add(address, (newBalance,newTime))
                    else
                        accumulator
                MergeInternal oldMap newMap tail newAcc

    let private Merge (oldMap: Map<'K, Cached<'V>>) (newMap: Map<'K, Cached<'V>>) =
        let addressList = Map.toList newMap |> List.map fst
        MergeInternal oldMap newMap addressList oldMap

    let public SaveSnapshot(newCachedData: CachedNetworkData) =
        lock lockObject (fun _ ->
            match sessionCachedNetworkData with
            | None ->
                sessionCachedNetworkData <- Some(newCachedData)
            | Some(networkData) ->
                let mergedBalances = Merge networkData.Balances newCachedData.Balances
                let mergedUsdPrices = Merge networkData.UsdPrice newCachedData.UsdPrice
                sessionCachedNetworkData <- Some({ Balances = mergedBalances; UsdPrice = mergedUsdPrices })

            SaveToDisk(sessionCachedNetworkData)
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

    let internal RetreiveLastBalance (address: string): NotFresh<decimal> =
        lock lockObject (fun _ ->
            match sessionCachedNetworkData with
            | None -> NotAvailable
            | Some(networkData) ->
                try
                    Cached(networkData.Balances.Item address)
                with
                | :? KeyNotFoundException -> NotAvailable
        )

    let internal StoreLastFiatUsdPrice (currency, lastFiatUsdPrice: decimal) =
        lock lockObject (fun _ ->
            let time = DateTime.Now

            let newCachedValue =
                match sessionCachedNetworkData with
                | None ->
                    Some({ UsdPrice = Map.empty.Add(currency, (lastFiatUsdPrice, time));
                           Balances = Map.empty})
                | Some(previousCachedData) ->
                    Some({ UsdPrice = previousCachedData.UsdPrice.Add(currency, (lastFiatUsdPrice, time));
                           Balances = previousCachedData.Balances })
            sessionCachedNetworkData <- newCachedValue

            SaveToDisk sessionCachedNetworkData
        )

    let internal StoreLastBalance (address: string, lastBalance: decimal) =
        lock lockObject (fun _ ->
            let time = DateTime.Now

            let newCachedValue =
                match sessionCachedNetworkData with
                | None ->
                    Some({ UsdPrice = Map.empty;
                           Balances = Map.empty.Add(address, (lastBalance, time))})
                | Some(previousCachedData) ->
                    Some({ UsdPrice = previousCachedData.UsdPrice;
                           Balances = previousCachedData.Balances.Add(address, (lastBalance, time)) })
            sessionCachedNetworkData <- newCachedValue

            SaveToDisk sessionCachedNetworkData
        )

