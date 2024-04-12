namespace GWallet.Backend

open System
open System.Net

open FSharp.Data
open Fsdk
open FSharpx.Collections

open GWallet.Backend.FSharpUtil.UwpHacks

module FiatValueEstimation =
    let private PERIOD_TO_CONSIDER_PRICE_STILL_FRESH = TimeSpan.FromMinutes 2.0

    type BitPayProvider = JsonProvider<"""
{
    "data":[
       {
          "code":"BTC",
          "name":"Bitcoin",
          "rate":1
       },
       {
          "code":"BCH",
          "name":"Bitcoin Cash",
          "rate":168.63
       },
       {
          "code":"USD",
          "name":"US Dollar",
          "rate":62765.91
       },
       {
          "code":"EUR",
          "name":"Eurozone Euro",
          "rate":57938.66
       },
       {
          "code":"GBP",
          "name":"Pound Sterling",
          "rate":49455.52
       },
       {
          "code":"JPY",
          "name":"Japanese Yen",
          "rate":9519768.47
       },
       {
          "code":"CAD",
          "name":"Canadian Dollar",
          "rate":85326.05
       },
       {
          "code":"AUD",
          "name":"Australian Dollar",
          "rate":96269.66
       },
       {
          "code":"CNY",
          "name":"Chinese Yuan",
          "rate":451801.58
       },
       {
          "code":"CHF",
          "name":"Swiss Franc",
          "rate":55900.26
       },
       {
          "code":"SEK",
          "name":"Swedish Krona",
          "rate":658411.89
       },
       {
          "code":"NZD",
          "name":"New Zealand Dollar",
          "rate":104038.2
       },
       {
          "code":"KRW",
          "name":"South Korean Won",
          "rate":84135078.75
       },
       {
          "code":"ETH",
          "name":"Ether",
          "rate":18.76
       },
       {
          "code":"MATIC_e",
          "name":"Matic",
          "rate":65109.87
       },
       {
          "code":"MATIC",
          "name":"Matic",
          "rate":65109.87
       },
       {
          "code":"ETH_m",
          "name":"Ether",
          "rate":18.76
       },
       {
          "code":"LTC",
          "name":"Litecoin",
          "rate":770.39
       },
       {
          "code":"XRP",
          "name":"Ripple",
          "rate":105416.29
       },
       {
          "code":"AED",
          "name":"UAE Dirham",
          "rate":230507.81
       },
       {
          "code":"AFN",
          "name":"Afghan Afghani",
          "rate":4482555.69
       },
       {
          "code":"ALL",
          "name":"Albanian Lek",
          "rate":5956146.2
       },
       {
          "code":"AMD",
          "name":"Armenian Dram",
          "rate":25189749.81
       },
       {
          "code":"ANG",
          "name":"Netherlands Antillean Guilder",
          "rate":113130.91
       },
       {
          "code":"AOA",
          "name":"Angolan Kwanza",
          "rate":52378152.65
       },
       {
          "code":"APE",
          "name":"ApeCoin",
          "rate":34167.62
       },
       {
          "code":"ARS",
          "name":"Argentine Peso",
          "rate":53557142.12
       },
       {
          "code":"AWG",
          "name":"Aruban Florin",
          "rate":113135.55
       },
       {
          "code":"AZN",
          "name":"Azerbaijani Manat",
          "rate":106702.05
       },
       {
          "code":"BAM",
          "name":"Bosnia-Herzegovina Convertible Mark",
          "rate":113225.12
       },
       {
          "code":"BBD",
          "name":"Barbadian Dollar",
          "rate":125531.82
       },
       {
          "code":"BDT",
          "name":"Bangladeshi Taka",
          "rate":6889115.33
       },
       {
          "code":"BGN",
          "name":"Bulgarian Lev",
          "rate":113239.12
       },
       {
          "code":"BHD",
          "name":"Bahraini Dinar",
          "rate":23655.22
       },
       {
          "code":"BIF",
          "name":"Burundian Franc",
          "rate":179563281.7
       },
       {
          "code":"BMD",
          "name":"Bermudan Dollar",
          "rate":62765.91
       },
       {
          "code":"BND",
          "name":"Brunei Dollar",
          "rate":84382.99
       },
       {
          "code":"BOB",
          "name":"Bolivian Boliviano",
          "rate":433742.76
       },
       {
          "code":"BRL",
          "name":"Brazilian Real",
          "rate":315348.49
       },
       {
          "code":"BSD",
          "name":"Bahamian Dollar",
          "rate":62765.91
       },
       {
          "code":"BTN",
          "name":"Bhutanese Ngultrum",
          "rate":5218257.97
       },
       {
          "code":"BUSD",
          "name":"Binance USD",
          "rate":62765.91
       },
       {
          "code":"BUSD_m",
          "name":"Binance USD",
          "rate":62765.91
       },
       {
          "code":"BWP",
          "name":"Botswanan Pula",
          "rate":859901.39
       },
       {
          "code":"BYN",
          "name":"Belarusian Ruble",
          "rate":205422.97
       },
       {
          "code":"BZD",
          "name":"Belize Dollar",
          "rate":126533.38
       },
       {
          "code":"CDF",
          "name":"Congolese Franc",
          "rate":174190026.14
       },
       {
          "code":"CLF",
          "name":"Chilean Unit of Account (UF)",
          "rate":2206.1
       },
       {
          "code":"CLP",
          "name":"Chilean Peso",
          "rate":60872891.03
       },
       {
          "code":"COP",
          "name":"Colombian Peso",
          "rate":244702271.64
       },
       {
          "code":"CRC",
          "name":"Costa Rican Colón",
          "rate":31587012.89
       },
       {
          "code":"CUP",
          "name":"Cuban Peso",
          "rate":1616222.21
       },
       {
          "code":"CVE",
          "name":"Cape Verdean Escudo",
          "rate":6383458.15
       },
       {
          "code":"CZK",
          "name":"Czech Koruna",
          "rate":1465079
       },
       {
          "code":"DAI",
          "name":"Dai",
          "rate":62772.19
       },
       {
          "code":"DAI_m",
          "name":"Dai",
          "rate":62772.19
       },
       {
          "code":"DJF",
          "name":"Djiboutian Franc",
          "rate":11178198.56
       },
       {
          "code":"DKK",
          "name":"Danish Krone",
          "rate":431675.38
       },
       {
          "code":"DOGE",
          "name":"Dogecoin",
          "rate":468611.45
       },
       {
          "code":"DOP",
          "name":"Dominican Peso",
          "rate":3709276.28
       },
       {
          "code":"DZD",
          "name":"Algerian Dinar",
          "rate":8445017.3
       },
       {
          "code":"EGP",
          "name":"Egyptian Pound",
          "rate":2947054.09
       },
       {
          "code":"ETB",
          "name":"Ethiopian Birr",
          "rate":3565334.28
       },
       {
          "code":"EUROC",
          "name":"EURC",
          "rate":57886.43
       },
       {
          "code":"FJD",
          "name":"Fijian Dollar",
          "rate":142761.06
       },
       {
          "code":"FKP",
          "name":"Falkland Islands Pound",
          "rate":49455.52
       },
       {
          "code":"GEL",
          "name":"Georgian Lari",
          "rate":170095.62
       },
       {
          "code":"GHS",
          "name":"Ghanaian Cedi",
          "rate":817175.5
       },
       {
          "code":"GIP",
          "name":"Gibraltar Pound",
          "rate":49455.52
       },
       {
          "code":"GMD",
          "name":"Gambian Dalasi",
          "rate":4261805.35
       },
       {
          "code":"GNF",
          "name":"Guinean Franc",
          "rate":539552473.67
       },
       {
          "code":"GTQ",
          "name":"Guatemalan Quetzal",
          "rate":489610.57
       },
       {
          "code":"GUSD",
          "name":"Gemini US Dollar",
          "rate":62765.91
       },
       {
          "code":"GYD",
          "name":"Guyanaese Dollar",
          "rate":13143518.92
       },
       {
          "code":"HKD",
          "name":"Hong Kong Dollar",
          "rate":491040.38
       },
       {
          "code":"HNL",
          "name":"Honduran Lempira",
          "rate":1549999.96
       },
       {
          "code":"HRK",
          "name":"Croatian Kuna",
          "rate":436175.82
       },
       {
          "code":"HTG",
          "name":"Haitian Gourde",
          "rate":8321935.04
       },
       {
          "code":"HUF",
          "name":"Hungarian Forint",
          "rate":22891020.63
       },
       {
          "code":"IDR",
          "name":"Indonesian Rupiah",
          "rate":987940571.5
       },
       {
          "code":"ILS",
          "name":"Israeli Shekel",
          "rate":230982
       },
       {
          "code":"INR",
          "name":"Indian Rupee",
          "rate":5222149.77
       },
       {
          "code":"IQD",
          "name":"Iraqi Dinar",
          "rate":82229763.1
       },
       {
          "code":"IRR",
          "name":"Iranian Rial",
          "rate":2638365064.68
       },
       {
          "code":"ISK",
          "name":"Icelandic Króna",
          "rate":8595791.5
       },
       {
          "code":"JEP",
          "name":"Jersey Pound",
          "rate":49455.52
       },
       {
          "code":"JMD",
          "name":"Jamaican Dollar",
          "rate":9670054.16
       },
       {
          "code":"JOD",
          "name":"Jordanian Dinar",
          "rate":44494.75
       },
       {
          "code":"KES",
          "name":"Kenyan Shilling",
          "rate":8505487.55
       },
       {
          "code":"KGS",
          "name":"Kyrgystani Som",
          "rate":5618176.68
       },
       {
          "code":"KHR",
          "name":"Cambodian Riel",
          "rate":254099106.3
       },
       {
          "code":"KMF",
          "name":"Comorian Franc",
          "rate":28442353
       },
       {
          "code":"KPW",
          "name":"North Korean Won",
          "rate":56489319.81
       },
       {
          "code":"KWD",
          "name":"Kuwaiti Dinar",
          "rate":19303.34
       },
       {
          "code":"KYD",
          "name":"Cayman Islands Dollar",
          "rate":52313.13
       },
       {
          "code":"KZT",
          "name":"Kazakhstani Tenge",
          "rate":28287063.36
       },
       {
          "code":"LAK",
          "name":"Laotian Kip",
          "rate":1314176973.42
       },
       {
          "code":"LBP",
          "name":"Lebanese Pound",
          "rate":5621500932.69
       },
       {
          "code":"LKR",
          "name":"Sri Lankan Rupee",
          "rate":19072711.62
       },
       {
          "code":"LRD",
          "name":"Liberian Dollar",
          "rate":12091852.36
       },
       {
          "code":"LSL",
          "name":"Lesotho Loti",
          "rate":1186155.02
       },
       {
          "code":"LYD",
          "name":"Libyan Dinar",
          "rate":302890.9
       },
       {
          "code":"MAD",
          "name":"Moroccan Dirham",
          "rate":630785.35
       },
       {
          "code":"MDL",
          "name":"Moldovan Leu",
          "rate":1114230.24
       },
       {
          "code":"MGA",
          "name":"Malagasy Ariary",
          "rate":281597829.63
       },
       {
          "code":"MKD",
          "name":"Macedonian Denar",
          "rate":3566159.52
       },
       {
          "code":"MMK",
          "name":"Myanma Kyat",
          "rate":131824969.85
       },
       {
          "code":"MNT",
          "name":"Mongolian Tugrik",
          "rate":216542392.61
       },
       {
          "code":"MOP",
          "name":"Macanese Pataca",
          "rate":505849.87
       },
       {
          "code":"MRU",
          "name":"Mauritanian Ouguiya",
          "rate":2498362.88
       },
       {
          "code":"MUR",
          "name":"Mauritian Rupee",
          "rate":2893508.12
       },
       {
          "code":"MVR",
          "name":"Maldivian Rufiyaa",
          "rate":976009.91
       },
       {
          "code":"MWK",
          "name":"Malawian Kwacha",
          "rate":105666955.43
       },
       {
          "code":"MXN",
          "name":"Mexican Peso",
          "rate":1056453.15
       },
       {
          "code":"MYR",
          "name":"Malaysian Ringgit",
          "rate":297384.89
       },
       {
          "code":"MZN",
          "name":"Mozambican Metical",
          "rate":4013880.06
       },
       {
          "code":"NAD",
          "name":"Namibian Dollar",
          "rate":1186160.54
       },
       {
          "code":"NGN",
          "name":"Nigerian Naira",
          "rate":94808536.07
       },
       {
          "code":"NIO",
          "name":"Nicaraguan Córdoba",
          "rate":2310554.15
       },
       {
          "code":"NOK",
          "name":"Norwegian Krone",
          "rate":670578.13
       },
       {
          "code":"NPR",
          "name":"Nepalese Rupee",
          "rate":8349200.62
       },
       {
          "code":"OMR",
          "name":"Omani Rial",
          "rate":24162.93
       },
       {
          "code":"PAB",
          "name":"Panamanian Balboa",
          "rate":62765.91
       },
       {
          "code":"PAX",
          "name":"Paxos Standard USD",
          "rate":62765.91
       },
       {
          "code":"PEN",
          "name":"Peruvian Nuevo Sol",
          "rate":232334.92
       },
       {
          "code":"PGK",
          "name":"Papua New Guinean Kina",
          "rate":236613.3
       },
       {
          "code":"PHP",
          "name":"Philippine Peso",
          "rate":3527726.64
       },
       {
          "code":"PKR",
          "name":"Pakistani Rupee",
          "rate":17474484.65
       },
       {
          "code":"PLN",
          "name":"Polish Zloty",
          "rate":250307.38
       },
       {
          "code":"PYG",
          "name":"Paraguayan Guarani",
          "rate":458466690.79
       },
       {
          "code":"QAR",
          "name":"Qatari Rial",
          "rate":229055.4
       },
       {
          "code":"RON",
          "name":"Romanian Leu",
          "rate":287825.64
       },
       {
          "code":"RSD",
          "name":"Serbian Dinar",
          "rate":6786194.05
       },
       {
          "code":"RUB",
          "name":"Russian Ruble",
          "rate":5817044.57
       },
       {
          "code":"RWF",
          "name":"Rwandan Franc",
          "rate":80727450.42
       },
       {
          "code":"SAR",
          "name":"Saudi Riyal",
          "rate":235395.7
       },
       {
          "code":"SBD",
          "name":"Solomon Islands Dollar",
          "rate":530650.94
       },
       {
          "code":"SCR",
          "name":"Seychellois Rupee",
          "rate":841826.75
       },
       {
          "code":"SDG",
          "name":"Sudanese Pound",
          "rate":36780823.79
       },
       {
          "code":"SGD",
          "name":"Singapore Dollar",
          "rate":84371.63
       },
       {
          "code":"SHIB",
          "name":"Shiba Inu",
          "rate":2457553285.04
       },
       {
          "code":"SHIB_m",
          "name":"Shiba Inu",
          "rate":2457553285.04
       },
       {
          "code":"SHP",
          "name":"Saint Helena Pound",
          "rate":49455.52
       },
       {
          "code":"SLL",
          "name":"Sierra Leonean Leone",
          "rate":1316169768.62
       },
       {
          "code":"SOS",
          "name":"Somali Shilling",
          "rate":35876983.87
       },
       {
          "code":"SRD",
          "name":"Surinamese Dollar",
          "rate":2211776.55
       },
       {
          "code":"STN",
          "name":"São Tomé and Príncipe Dobra",
          "rate":1418405.96
       },
       {
          "code":"SVC",
          "name":"Salvadoran Colón",
          "rate":549218.73
       },
       {
          "code":"SYP",
          "name":"Syrian Pound",
          "rate":157701234.11
       },
       {
          "code":"SZL",
          "name":"Swazi Lilangeni",
          "rate":1186375.01
       },
       {
          "code":"THB",
          "name":"Thai Baht",
          "rate":2270458.22
       },
       {
          "code":"TJS",
          "name":"Tajikistani Somoni",
          "rate":687996.36
       },
       {
          "code":"TMT",
          "name":"Turkmenistani Manat",
          "rate":220308.35
       },
       {
          "code":"TND",
          "name":"Tunisian Dinar",
          "rate":194935.23
       },
       {
          "code":"TOP",
          "name":"Tongan Paʻanga",
          "rate":148941.31
       },
       {
          "code":"TRY",
          "name":"Turkish Lira",
          "rate":2032881.21
       },
       {
          "code":"TTD",
          "name":"Trinidad and Tobago Dollar",
          "rate":426100.7
       },
       {
          "code":"TWD",
          "name":"New Taiwan Dollar",
          "rate":2002910.43
       },
       {
          "code":"TZS",
          "name":"Tanzanian Shilling",
          "rate":160178604.62
       },
       {
          "code":"UAH",
          "name":"Ukrainian Hryvnia",
          "rate":2457258.05
       },
       {
          "code":"UGX",
          "name":"Ugandan Shilling",
          "rate":243263175.03
       },
       {
          "code":"USDC",
          "name":"USDC",
          "rate":62765.91
       },
       {
          "code":"USDC_m",
          "name":"USDC.e",
          "rate":62765.91
       },
       {
          "code":"USDCn_m",
          "name":"USDC",
          "rate":62765.91
       },
       {
          "code":"USDT",
          "name":"Tether",
          "rate":62797.94
       },
       {
          "code":"USDT_m",
          "name":"Tether",
          "rate":62797.94
       },
       {
          "code":"PYUSD",
          "name":"PayPal USD",
          "rate":62765.91
       },
       {
          "code":"UYU",
          "name":"Uruguayan Peso",
          "rate":2410622.28
       },
       {
          "code":"UZS",
          "name":"Uzbekistan Som",
          "rate":788558978.1
       },
       {
          "code":"VES",
          "name":"Venezuelan Bolívar Soberano",
          "rate":2273380.92
       },
       {
          "code":"VND",
          "name":"Vietnamese Dong",
          "rate":1554573859.14
       },
       {
          "code":"VUV",
          "name":"Vanuatu Vatu",
          "rate":7451694.47
       },
       {
          "code":"WBTC",
          "name":"Wrapped BTC",
          "rate":0.989822
       },
       {
          "code":"WBTC_m",
          "name":"Wrapped BTC",
          "rate":0.989822
       },
       {
          "code":"WST",
          "name":"Samoan Tala",
          "rate":175744.55
       },
       {
          "code":"XAF",
          "name":"CFA Franc BEAC",
          "rate":37971011.45
       },
       {
          "code":"XAG",
          "name":"Silver (troy ounce)",
          "rate":2524.62
       },
       {
          "code":"XAU",
          "name":"Gold (troy ounce)",
          "rate":29.14
       },
       {
          "code":"XCD",
          "name":"East Caribbean Dollar",
          "rate":169628.01
       },
       {
          "code":"XPF",
          "name":"CFP Franc",
          "rate":6907689.02
       },
       {
          "code":"XOF",
          "name":"CFA Franc BCEAO",
          "rate":37971011.45
       },
       {
          "code":"YER",
          "name":"Yemeni Rial",
          "rate":15715018.02
       },
       {
          "code":"ZAR",
          "name":"South African Rand",
          "rate":1185012.49
       },
       {
          "code":"ZMW",
          "name":"Zambian Kwacha",
          "rate":1622714.33
       },
       {
          "code":"ZWL",
          "name":"Zimbabwean Dollar",
          "rate":20210623.31
       }
    ]
}""">

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
        | BitPay
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
                | PriceProvider.BitPay ->
                    "https://bitpay.com/rates"
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

    let private QueryBitPay currency =
        async {
             let! maybeJson = QueryOnlineInternal currency PriceProvider.BitPay
             match maybeJson with
             | None -> return None
             | Some (_, json) ->

                let tickersObj = BitPayProvider.Parse json

                let getRate (currencyTicker: string): Option<decimal> =
                    match Seq.tryFind (fun (elem: BitPayProvider.Datum) -> elem.Code = currencyTicker) tickersObj.Data with
                    | None -> None
                    | Some rate -> Some rate.Rate

                let getUsdRate () =
                    getRate "USD"

                match currency with
                | BTC ->
                    let usdRate = getUsdRate()
                    (*
                    match usdRate with
                    | None ->
                        Infrastructure.LogDebug(SPrintF1 "BitPay providing fiat rate for %A: NONE" currency)
                    | Some rate ->
                        Infrastructure.LogDebug(SPrintF2 "BitPay providing fiat rate for %A: %s" currency (rate.ToString()))
                    *)
                    return usdRate
                | altCoin ->
                    let altCoinRateInBtcTerms = getRate (altCoin.ToString())
                    let leUsdRate = getUsdRate()
                    match altCoinRateInBtcTerms, leUsdRate with
                    | Some altcoinRate, Some usdRate ->
                        let rate = usdRate / altcoinRate
                        //Infrastructure.LogDebug(SPrintF2 "BitPay providing fiat rate for %A: %s" currency (rate.ToString()))
                        return Some rate
                    | _ ->
                        //Infrastructure.LogDebug(SPrintF1 "BitPay providing fiat rate for %A: NONE" currency)
                        return None
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
        let bitPayJob = QueryBitPay currency
        let coinGeckoJob = QueryCoinGecko currency
        let coinCapJob = QueryCoinCap currency

        let multiCurrencyJobs = [ coinGeckoJob; coinCapJob; bitPayJob ]
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

