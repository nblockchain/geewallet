namespace GWallet.Backend

open System
open System.Net

open FSharp.Data

module FiatValueEstimation =
    let private PERIOD_TO_CONSIDER_PRICE_STILL_FRESH = TimeSpan.FromMinutes 2.0

    type CoinMarketCapJsonProvider = JsonProvider<"""
[
    {
        "id": "ethereum-classic",
        "name": "Ethereum Classic",
        "symbol": "ETC",
        "rank": "7",
        "price_usd": "6.53227",
        "price_btc": "0.00371428",
        "24h_volume_usd": "30538600.0",
        "market_cap_usd": "598575475.0",
        "available_supply": "91633609.0",
        "total_supply": "91633609.0",
        "percent_change_1h": "-1.82",
        "percent_change_24h": "2.45",
        "percent_change_7d": "-6.04",
        "last_updated": "1494822573"
    }
]
    """>

    let private RetreiveOnlineInternal currency: Option<string> =
        use webClient = new WebClient()
        let tickerName =
            match currency with
            | Currency.BTC -> "bitcoin"
            | Currency.LTC -> "litecoin"
            | Currency.ETH -> "ethereum"
            | Currency.ETC -> "ethereum-classic"
            | Currency.DAI -> "dai"

        try
            webClient.DownloadString(sprintf "https://api.coinmarketcap.com/v1/ticker/%s/" tickerName)
                |> Some
        with
        | :? WebException -> None

    let private ParseJsonStoringInCache currency (json: string) =
        let ticker = CoinMarketCapJsonProvider.Parse(json)
        if (ticker.Length <> 1) then
            failwith ("Unexpected length of json main array: " + json)

        let usdPrice = ticker.[0].PriceUsd
        let result = usdPrice
        Caching.Instance.StoreLastFiatUsdPrice(currency, usdPrice)
        result

    let private RetreiveOnline currency: Option<decimal> =
        match RetreiveOnlineInternal currency with
        | None -> None
        | Some json ->
            Some (ParseJsonStoringInCache currency json)

    let UsdValue(currency: Currency): MaybeCached<decimal> =
        let maybeUsdPrice = Caching.Instance.RetreiveLastKnownUsdPrice currency
        match maybeUsdPrice with
        | NotAvailable ->
            match RetreiveOnline currency with
            | None -> NotFresh NotAvailable
            | Some value -> Fresh value
        | Cached(someValue,someDate) ->
            if (someDate + PERIOD_TO_CONSIDER_PRICE_STILL_FRESH) > DateTime.Now then
                Fresh someValue
            else
                match RetreiveOnline currency with
                | None ->
                    NotFresh (Cached(someValue,someDate))
                | Some freshValue ->
                    Fresh freshValue

