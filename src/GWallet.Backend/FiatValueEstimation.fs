namespace GWallet.Backend

open System
open System.Net

open FSharp.Data

open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks

module FiatValueEstimation =
    let private PERIOD_TO_CONSIDER_PRICE_STILL_FRESH = TimeSpan.FromMinutes 2.0

    type CoinCapProvider = JsonProvider<"""
    {
      "data": {
        "id": "bitcoin",
        "symbol": "BTC",
        "currencySymbol": "x",
        "type": "crypto",
        "rateUsd": "6444.3132749056076909"
      },
      "timestamp": 1536347871542
    }
    """>

    type PriceProvider =
        | CoinCap
        | CoinGecko

    let private QueryOnlineInternal currency (provider: PriceProvider): Async<Maybe<string*string>> = async {
        use webClient = new WebClient()
        let tickerName =
            match currency,provider with
            | Currency.BTC,_ -> "bitcoin"
            | Currency.LTC,_ -> "litecoin"
            | Currency.ETH,_ -> "ethereum"
            | Currency.ETC,_ -> "ethereum-classic"
            | Currency.DAI,PriceProvider.CoinCap -> "multi-collateral-dai"
            | Currency.DAI,_ -> "dai"
            // the API of CoinCap is not returning anything for "sai" (even if the API from coingecko does) or "single-collateral-dai"
            | Currency.SAI,PriceProvider.CoinCap -> "multi-collateral-dai"
            | Currency.SAI,_ -> "sai"

        try
            let baseUrl =
                match provider with
                | PriceProvider.CoinCap ->
                    SPrintF1 "https://api.coincap.io/v2/rates/%s" tickerName
                | PriceProvider.CoinGecko ->
                    SPrintF1 "https://api.coingecko.com/api/v3/simple/price?ids=%s&vs_currencies=usd" tickerName
            let uri = Uri baseUrl
            let task = webClient.DownloadStringTaskAsync uri
            let! res = Async.AwaitTask task
            return Just (tickerName,res)
        with
        | ex ->
            if (FSharpUtil.FindException<WebException> ex).IsJust then
                return Nothing
            else
                return raise <| FSharpUtil.ReRaise ex
    }

    let private QueryCoinCap currency = async {
        let! maybeJson = QueryOnlineInternal currency PriceProvider.CoinCap
        match maybeJson with
        | Nothing -> return Nothing
        | Just (_, json) ->
            try
                let tickerObj = CoinCapProvider.Parse json
                return Just tickerObj.Data.RateUsd
            with
            | ex ->
                if currency = ETC then
                    // interestingly this can throw in CoinCap because retreiving ethereum-classic doesn't work...
                    return Nothing
                else
                    return raise <| FSharpUtil.ReRaise ex
    }

    let private QueryCoinGecko currency = async {
        let! maybeJson = QueryOnlineInternal currency PriceProvider.CoinGecko
        match maybeJson with
        | Nothing -> return Nothing
        | Just (ticker, json) ->
            // try to parse this as an example: {"bitcoin":{"usd":7952.29}}
            let parsedJsonObj = FSharp.Data.JsonValue.Parse json
            let usdPrice =
                match parsedJsonObj.TryGetProperty ticker |> Maybe.OfOpt with
                | Nothing -> failwith <| SPrintF1 "Could not pre-parse %s" json
                | Just innerObj ->
                    match innerObj.TryGetProperty "usd" |> Maybe.OfOpt with
                    | Nothing -> failwith <| SPrintF1 "Could not parse %s" json
                    | Just value -> value.AsDecimal()
            return Just usdPrice
    }

    let private RetrieveOnline currency = async {
        let coinGeckoJob = QueryCoinGecko currency
        let coinCapJob = QueryCoinCap currency
        let bothJobs = FSharpUtil.AsyncExtensions.MixedParallel2 coinGeckoJob coinCapJob
        let! maybeUsdPriceFromCoinGecko, maybeUsdPriceFromCoinCap = bothJobs
        if maybeUsdPriceFromCoinCap.IsJust && currency = Currency.ETC then
            Infrastructure.ReportWarningMessage "Currency ETC can now be queried from CoinCap provider?"
        match maybeUsdPriceFromCoinGecko, maybeUsdPriceFromCoinCap with
        | Nothing, Nothing -> return Nothing
        | Just usdPriceFromCoinGecko, Nothing ->
            Caching.Instance.StoreLastFiatUsdPrice(currency, usdPriceFromCoinGecko)
            return Just usdPriceFromCoinGecko
        | Nothing, Just usdPriceFromCoinCap ->
            Caching.Instance.StoreLastFiatUsdPrice(currency, usdPriceFromCoinCap)
            return Just usdPriceFromCoinCap
        | Just usdPriceFromCoinGecko, Just usdPriceFromCoinCap ->
            let average = (usdPriceFromCoinGecko + usdPriceFromCoinCap) / 2m
            Caching.Instance.StoreLastFiatUsdPrice(currency, average)
            return Just average
    }

    let UsdValue(currency: Currency): Async<MaybeCached<decimal>> = async {
        let maybeUsdPrice = Caching.Instance.RetrieveLastKnownUsdPrice currency
        match maybeUsdPrice with
        | NotAvailable ->
            let! maybeOnlineUsdPrice = RetrieveOnline currency
            match maybeOnlineUsdPrice with
            | Nothing -> return NotFresh NotAvailable
            | Just value -> return Fresh value
        | Cached(someValue,someDate) ->
            if (someDate + PERIOD_TO_CONSIDER_PRICE_STILL_FRESH) > DateTime.UtcNow then
                return Fresh someValue
            else
                let! maybeOnlineUsdPrice = RetrieveOnline currency
                match maybeOnlineUsdPrice with
                | Nothing ->
                    return NotFresh (Cached(someValue,someDate))
                | Just freshValue ->
                    return Fresh freshValue
    }

