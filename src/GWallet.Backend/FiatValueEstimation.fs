namespace GWallet.Backend

open System
open System.Net

open FSharp.Data

module FiatValueEstimation =
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

    let UsdValue(currency: Currency): MaybeCached<decimal> =
        use webClient = new WebClient()
        let tickerName =
            match currency with
            | Currency.BTC -> "bitcoin"
            | Currency.LTC -> "litecoin"
            | Currency.ETH -> "ethereum"
            | Currency.ETC -> "ethereum-classic"
            | Currency.DAI -> "dai"

        let maybeJson =
            try
                Some(webClient.DownloadString(sprintf "https://api.coinmarketcap.com/v1/ticker/%s/" tickerName))
            with
            | :? WebException -> None

        match maybeJson with
        | None ->
            NotFresh(Caching.RetreiveLastKnownUsdPrice(currency))
        | Some(json) ->
            let ticker = CoinMarketCapJsonProvider.Parse(json)
            if (ticker.Length <> 1) then
                failwith ("Unexpected length of json main array: " + json)

            let usdPrice = ticker.[0].PriceUsd
            let result = usdPrice
            Caching.StoreLastFiatUsdPrice(currency, usdPrice)
            Fresh(result)
