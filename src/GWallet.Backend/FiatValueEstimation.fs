namespace GWallet.Backend

open System
open System.Net

open FSharp.Data
open Fsdk
open FSharpx.Collections

open GWallet.Backend.FSharpUtil.UwpHacks

module FiatValueEstimation =
    let private PERIOD_TO_CONSIDER_PRICE_STILL_FRESH = TimeSpan.FromMinutes 2.0

    type CoinCapProvider = JsonProvider<"""
    {
      "data": {
        "id": "bitcoin",
        "rank": "1",
        "symbol": "BTC",
        "name": "Bitcoin",
        "supply": "19281750.0000000000000000",
        "maxSupply": "21000000.0000000000000000",
        "marketCapUsd": "450487811256.3001791347094000",
        "volumeUsd24Hr": "6522579119.6754297896356342",
        "priceUsd": "23363.4297331051475688",
        "changePercent24Hr": "-0.0049558876182867",
        "vwap24Hr": "23430.6497382042627725",
        "explorer": "https://blockchain.info/"
      },
      "timestamp": 1675571191130
    }
    """>

    type CoindDeskProvider = JsonProvider<"""
    {
        "time": {
            "updated": "Feb 25, 2024 12:27:26 UTC",
            "updatedISO": "2024-02-25T12:27:26+00:00",
            "updateduk": "Feb 25, 2024 at 12:27 GMT"
        },
        "disclaimer":"This data was produced from the CoinDesk Bitcoin Price Index (USD). Non-USD currency data converted using hourly conversion rate from openexchangerates.org",
        "chartName":"Bitcoin",
        "bpi": {
            "USD": {
                "code": "USD",
                "symbol": "&#36;",
                "rate": "51,636.062",
                "description": "United States Dollar",
                "rate_float": 51636.0621
            },
            "GBP": {
                "code": "GBP",
                "symbol": "&pound;",
                "rate": "40,725.672",
                "description": "British Pound Sterling",
                "rate_float": 40725.672
            },
            "EUR": {
                "code":"EUR",
                "symbol": "&euro;",
                "rate":"47,654.25",
                "description": "Euro",
                "rate_float": 47654.2504
            }
        }
    }
    """>

    type PriceProvider =
        | CoinCap
        | CoinGecko
        | CoinDesk

    let private QueryOnlineInternal currency (provider: PriceProvider): Async<Option<string*string>> = async {
        use webClient = new WebClient()
        let tickerName =
            match currency,provider with
            | Currency.BTC,_ -> "bitcoin"
            | Currency.LTC,_ -> "litecoin"

            // NOTE: don't worry, a second calculation will be performed for SAI, see https://github.com/nblockchain/geewallet/commit/bb7f59271b21d1ab278e4d4dcd9e12a3bdd49ba9
            | Currency.ETH,_ | Currency.SAI,_ -> "ethereum"

            | Currency.ETC,_ -> "ethereum-classic"
            | Currency.DAI,PriceProvider.CoinCap -> "multi-collateral-dai"
            | Currency.DAI,_ -> "dai"

        try
            let baseUrl =
                match provider with
                | PriceProvider.CoinCap ->
                    SPrintF1 "https://api.coincap.io/v2/assets/%s" tickerName
                | PriceProvider.CoinGecko ->
                    SPrintF1 "https://api.coingecko.com/api/v3/simple/price?ids=%s&vs_currencies=usd" tickerName
                | PriceProvider.CoinDesk ->
                    if currency <> Currency.BTC then
                        failwith "CoinDesk API only provides bitcoin price"
                    "https://api.coindesk.com/v1/bpi/currentprice.json"
            let uri = Uri baseUrl
            let task = webClient.DownloadStringTaskAsync uri
            let! res = Async.AwaitTask task
            return Some (tickerName,res)
        with
        | ex ->
            if (FSharpUtil.FindException<WebException> ex).IsSome then
                return None
            else
                return raise <| FSharpUtil.ReRaise ex
    }

    let private QueryCoinCap currency = async {
        let! maybeJson = QueryOnlineInternal currency PriceProvider.CoinCap
        match maybeJson with
        | None -> return None
        | Some (_, json) ->
            try
                let tickerObj = CoinCapProvider.Parse json
                return Some tickerObj.Data.PriceUsd
            with
            | ex ->
                return raise (Exception(SPrintF2 "Could not parse CoinCap's JSON (for %A): %s" currency json, ex))
    }

    let private QueryCoinDesk(): Async<Option<decimal>> =
        async {
            let! maybeJson = QueryOnlineInternal Currency.BTC PriceProvider.CoinDesk
            match maybeJson with
            | None -> return None
            | Some (_, json) ->
                try
                    let tickerObj = CoindDeskProvider.Parse json
                    return Some tickerObj.Bpi.Usd.RateFloat
                with
                | ex ->
                    return raise <| Exception (SPrintF1 "Could not parse CoinDesk's JSON: %s" json, ex)
        }

    let private QueryCoinGecko currency = async {
        let! maybeJson = QueryOnlineInternal currency PriceProvider.CoinGecko
        match maybeJson with
        | None -> return None
        | Some (ticker, json) ->
            // try to parse this as an example: {"bitcoin":{"usd":7952.29}}
            let parsedJsonObj = FSharp.Data.JsonValue.Parse json
            let usdPrice =
                match parsedJsonObj.TryGetProperty ticker with
                | None -> failwith <| SPrintF2 "Could not pre-parse CoinGecko's JSON (for %A): %s" currency json
                | Some innerObj ->
                    match innerObj.TryGetProperty "usd" with
                    | None -> failwith <| SPrintF2 "Could not parse CoinGecko's JSON (for %A): %s" currency json
                    | Some value -> value.AsDecimal()
            return Some usdPrice
    }

    let MaybeReportAbnormalDifference (result1: decimal) (result2: decimal) currency =
        let higher = Math.Max(result1, result2)
        let lower = Math.Min(result1, result2)

        // example: 100USD vs: 66.666USD (or lower)
        let abnormalDifferenceRate = 1.5m
        if (higher / lower) > abnormalDifferenceRate then
            let err =
                SPrintF4 "Alert: difference of USD exchange rate (for %A) between the providers is abnormally high: %M vs %M (H/L > %M)"
                    currency
                    result1
                    result2
                    abnormalDifferenceRate
#if DEBUG
            failwith err
#else
            Infrastructure.ReportError err
            |> ignore<bool>
#endif

    let private Average (results: seq<Option<decimal>>) currency =
        let rec averageInternal (nextResults: seq<Option<decimal>>) (resultSoFar: Option<decimal>) (resultCountSoFar: uint32) =
            match Seq.tryHeadTail nextResults with
            | None ->
                match resultSoFar with
                | None ->
                    None
                | Some res ->
                    (res / (decimal (int resultCountSoFar))) |> Some
            | Some(head, tail) ->
                match head with
                | None ->
                    averageInternal tail resultSoFar resultCountSoFar
                | Some res ->
                    match resultSoFar with
                    | None ->
                        if resultCountSoFar <> 0u then
                            failwith <| SPrintF1 "Got resultSoFar==None but resultCountSoFar>0u: %i" (int resultCountSoFar)
                        averageInternal tail (Some res) 1u
                    | Some prevRes ->
                        let averageSoFar = prevRes / (decimal (int resultCountSoFar))

                        MaybeReportAbnormalDifference averageSoFar res currency

                        averageInternal tail (Some (prevRes + res)) (resultCountSoFar + 1u)
        averageInternal results None 0u

    let private RetrieveOnline currency = async {
        let coinGeckoJob = QueryCoinGecko currency
        let coinCapJob = QueryCoinCap currency

        let multiCurrencyJobs = [ coinGeckoJob; coinCapJob ]
        let allJobs =
            match currency with
            | Currency.BTC ->
                let coinDeskJob = QueryCoinDesk()
                coinDeskJob :: multiCurrencyJobs
            | _ ->
                multiCurrencyJobs

        let! allResults = Async.Parallel allJobs
        let result = Average allResults currency

        let realResult =
            match result with
            | Some price ->
                let realPrice =
                    if currency = Currency.SAI then
                        let ethMultiplied = price * 0.0053m
                        ethMultiplied
                    else
                        price
                Caching.Instance.StoreLastFiatUsdPrice(currency, realPrice)
                realPrice |> Some
            | None -> None
        return realResult
    }

    let UsdValue(currency: Currency): Async<MaybeCached<decimal>> = async {
        let maybeUsdPrice = Caching.Instance.RetrieveLastKnownUsdPrice currency
        match maybeUsdPrice with
        | NotAvailable ->
            let! maybeOnlineUsdPrice = RetrieveOnline currency
            match maybeOnlineUsdPrice with
            | None -> return NotFresh NotAvailable
            | Some value -> return Fresh value
        | Cached(someValue,someDate) ->
            if (someDate + PERIOD_TO_CONSIDER_PRICE_STILL_FRESH) > DateTime.UtcNow then
                return Fresh someValue
            else
                let! maybeOnlineUsdPrice = RetrieveOnline currency
                match maybeOnlineUsdPrice with
                | None ->
                    return NotFresh (Cached(someValue,someDate))
                | Some freshValue ->
                    return Fresh freshValue
    }

    let SmallestFiatFeeThatIsNoLongerRidiculous = 0.01m

