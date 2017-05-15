namespace GWallet.Backend

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

    let UsdValue(currency: Currency) =
        use webClient = new WebClient()
        let tickerName =
            match currency with
            | Currency.ETH -> "ethereum"
            | Currency.ETC -> "ethereum-classic"
            | _ -> "Unsupported currency"
        let json = webClient.DownloadString(sprintf "https://api.coinmarketcap.com/v1/ticker/%s/" tickerName)
        let ticker = CoinMarketCapJsonProvider.Parse(json)
        if (ticker.Length <> 1) then
            failwith ("Unexpected length of json main array: " + json)
        ticker.[0].PriceUsd
