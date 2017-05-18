namespace GWallet.Backend

open System
open System.IO
open System.Collections.Generic

open Newtonsoft.Json

type MaybeStoredInCache<'T> =
    NotAvailable | Cached of 'T*DateTime

type MaybeLoadedFromCache<'T> =
    NotFresh of MaybeStoredInCache<'T> | Fresh of 'T

type CachedNetworkData =
    {
        UsdPrice: Map<Currency,MaybeStoredInCache<decimal>>;
        Balances: Map<string,MaybeStoredInCache<decimal>>
    }

module internal Caching =

    let private GetCacheDir() =
        let configPath = Config.GetConfigDirForThisProgram().FullName
        let configDir = DirectoryInfo(Path.Combine(configPath, "cache"))
        if not configDir.Exists then
            configDir.Create()
        configDir

    let private lastCacheFile = Path.Combine(GetCacheDir().FullName, "last.json")

    let private LoadFromDisk (): Option<CachedNetworkData> =
        try
            let json = File.ReadAllText(lastCacheFile)
            let deserializedJson =
                JsonConvert.DeserializeObject<CachedNetworkData>(json, FSharpUtil.OptionConverter())
            Some(deserializedJson)
        with
        | :? FileNotFoundException -> None

    let mutable private sessionCachedNetworkData: Option<CachedNetworkData> = LoadFromDisk ()
    let private lockObject = Object()

    let private SaveToDisk (newCachedData: Option<CachedNetworkData>) =
        let json =
            JsonConvert.SerializeObject(sessionCachedNetworkData, FSharpUtil.OptionConverter())
        File.WriteAllText(lastCacheFile, json)

    let RetreiveLastFiatUsdPrice (currency): MaybeStoredInCache<decimal> =
        lock lockObject (fun _ ->
            match sessionCachedNetworkData with
            | None -> NotAvailable
            | Some(networkData) ->
                try
                    networkData.UsdPrice.Item currency
                with
                | :? KeyNotFoundException -> NotAvailable
        )

    let RetreiveLastBalance (address: string): MaybeStoredInCache<decimal> =
        lock lockObject (fun _ ->
            match sessionCachedNetworkData with
            | None -> NotAvailable
            | Some(networkData) ->
                try
                    networkData.Balances.Item address
                with
                | :? KeyNotFoundException -> NotAvailable
        )

    let StoreLastFiatUsdPrice (currency, lastFiatUsdPrice: decimal) =
        lock lockObject (fun _ ->
            let time = DateTime.Now

            let newCachedValue =
                match sessionCachedNetworkData with
                | None ->
                    Some({ UsdPrice = Map.empty.Add(currency, Cached(lastFiatUsdPrice, time));
                           Balances = Map.empty})
                | Some(previousCachedData) ->
                    Some({ UsdPrice = previousCachedData.UsdPrice.Add(currency, Cached(lastFiatUsdPrice, time));
                           Balances = previousCachedData.Balances })
            sessionCachedNetworkData <- newCachedValue

            SaveToDisk sessionCachedNetworkData
        )

    let StoreLastBalance (address: string, lastBalance: decimal) =
        lock lockObject (fun _ ->
            let time = DateTime.Now

            let newCachedValue =
                match sessionCachedNetworkData with
                | None ->
                    Some({ UsdPrice = Map.empty;
                           Balances = Map.empty.Add(address, Cached(lastBalance, time))})
                | Some(previousCachedData) ->
                    Some({ UsdPrice = previousCachedData.UsdPrice;
                           Balances = previousCachedData.Balances.Add(address, Cached(lastBalance, time)) })
            sessionCachedNetworkData <- newCachedValue

            SaveToDisk sessionCachedNetworkData
        )

