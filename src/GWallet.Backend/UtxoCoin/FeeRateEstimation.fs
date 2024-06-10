namespace GWallet.Backend.UtxoCoin

open System
open System.Linq

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks

open FSharp.Data
open NBitcoin

module FeeRateEstimation =

    type Priority =
    | Highest
    | Low

    type MempoolSpaceProvider = JsonProvider<"""
        {
          "fastestFee": 41,
          "halfHourFee": 38,
          "hourFee": 35,
          "economyFee": 12,
          "minimumFee": 6
        }
    """>

    let MempoolSpaceRestApiUri = Uri "https://mempool.space/api/v1/fees/recommended"

    type BlockchainInfoProvider = JsonProvider<"""
        {
          "limits": {
            "min": 4,
            "max": 16
          },
          "regular": 9,
          "priority": 11
        }
    """>

    let BlockchainInfoRestApiUri = Uri "https://api.blockchain.info/mempool/fees"

    let private ToBrandedType(feeRatePerKB: decimal) (moneyUnit: MoneyUnit): FeeRate =
        try
            Money(feeRatePerKB, moneyUnit) |> FeeRate
        with
        | ex ->
            // we need more info in case this bug shows again: https://gitlab.com/nblockchain/geewallet/issues/43
            raise <| Exception(SPrintF2 "Could not create fee rate from %s %A"
                                        (feeRatePerKB.ToString()) moneyUnit, ex)

    let private QueryFeeRateToMempoolSpace (priority: Priority): Async<Option<FeeRate>> =
        async {
            let! maybeJson = Networking.QueryRestApi MempoolSpaceRestApiUri
            match maybeJson with
            | None -> return None
            | Some json ->
                let recommendedFees = MempoolSpaceProvider.Parse json
                let highPrioFeeSatsPerB =
                    // FIXME: at the moment of writing this, .FastestFee is even higher than what electrum servers recommend (15 vs 12)
                    // (and .MinimumFee and .EconomyFee (3,6) seem too low, given that mempool.space website (not API) was giving 10,11,12)
                    match priority with
                    | Highest -> recommendedFees.FastestFee
                    | Low -> recommendedFees.EconomyFee
                    |> decimal
                Infrastructure.LogDebug (SPrintF1 "mempool.space API gave us a fee rate of %M sat per B" highPrioFeeSatsPerB)
                let satPerKB = highPrioFeeSatsPerB * (decimal 1000)
                return Some <| ToBrandedType satPerKB MoneyUnit.Satoshi
        }

    let private QueryFeeRateToBlockchainInfo (priority: Priority): Async<Option<FeeRate>> =
        async {
            let! maybeJson = Networking.QueryRestApi BlockchainInfoRestApiUri
            match maybeJson with
            | None -> return None
            | Some json ->
                let recommendedFees = BlockchainInfoProvider.Parse json
                let highPrioFeeSatsPerB =
                    // FIXME: at the moment of writing this, both priority & regular give same number wtaf -> 9
                    // (and .Limits.Min was 4, which seemed too low given that mempool.space website (not API) was giving 10,11,12;
                    //  and .Limits.Max was too high, higher than what electrum servers were suggesting: 12)
                    match priority with
                    | Highest -> recommendedFees.Priority
                    | Low -> recommendedFees.Regular
                    |> decimal
                Infrastructure.LogDebug (SPrintF1 "blockchain.info API gave us a fee rate of %M sat per B" highPrioFeeSatsPerB)
                let satPerKB = highPrioFeeSatsPerB * (decimal 1000)
                return Some <| ToBrandedType satPerKB MoneyUnit.Satoshi
        }

    let private AverageFee (feesFromDifferentServers: List<decimal>): decimal =
        let avg = feesFromDifferentServers.Sum() / decimal feesFromDifferentServers.Length
        avg

    let private QueryFeeRateToElectrumServers (currency: Currency) (priority: Priority): Async<FeeRate> =
        async {
            //querying for 1 will always return -1 surprisingly...
            let numBlocksToWait =
                match currency, priority with
                | Currency.BTC, Low ->
                    6
                | Currency.LTC, _
                | _, Highest ->
                    //querying for 1 will always return -1 surprisingly...
                    2
                | otherCurrency, otherPrio ->
                    failwith <| SPrintF2 "UTXO-based currency %A not implemented ElectrumServer feeRate %A query" otherCurrency otherPrio

            let estimateFeeJob = ElectrumClient.EstimateFee numBlocksToWait
            let! btcPerKiloByteForFastTrans =
                Server.Query currency (QuerySettings.FeeEstimation AverageFee) estimateFeeJob None
            return ToBrandedType (decimal btcPerKiloByteForFastTrans) MoneyUnit.BTC
        }

    let QueryFeeRateInternal currency (priority: Priority) =
        let electrumJob =
            async {
                try
                    let! result = QueryFeeRateToElectrumServers currency priority
                    return Some result
                with
                | :? NoneAvailableException ->
                    return None
            }

        async {
            match currency with
            | Currency.LTC ->
                let! electrumResult = electrumJob
                return electrumResult
            | Currency.BTC ->
                let! bothJobs = Async.Parallel [electrumJob; QueryFeeRateToMempoolSpace priority; QueryFeeRateToBlockchainInfo priority]
                let electrumResult = bothJobs.ElementAt 0
                let mempoolSpaceResult = bothJobs.ElementAt 1
                let blockchainInfoResult = bothJobs.ElementAt 2
                match electrumResult, mempoolSpaceResult, blockchainInfoResult with
                | None, None, None -> return None
                | Some feeRate, None, None ->
                    Infrastructure.LogDebug "Only electrum servers available for feeRate estimation"
                    return Some feeRate
                | None, Some feeRate, None ->
                    Infrastructure.LogDebug "Only mempool.space API available for feeRate estimation"
                    return Some feeRate
                | None, None, Some feeRate ->
                    Infrastructure.LogDebug "Only blockchain.info API available for feeRate estimation"
                    return Some feeRate
                | None, Some restApiFeeRate1, Some restApiFeeRate2 ->
                    Infrastructure.LogDebug "Only REST APIs available for feeRate estimation"
                    let average = AverageFee [decimal restApiFeeRate1.FeePerK.Satoshi; decimal restApiFeeRate2.FeePerK.Satoshi]
                    let averageFeeRate = ToBrandedType average MoneyUnit.Satoshi
                    Infrastructure.LogDebug (SPrintF1 "Average fee rate of %M sat per B" averageFeeRate.SatoshiPerByte)
                    return Some averageFeeRate
                | Some electrumFeeRate, Some restApiFeeRate, None ->
                    let average = AverageFee [decimal electrumFeeRate.FeePerK.Satoshi; decimal restApiFeeRate.FeePerK.Satoshi]
                    let averageFeeRate = ToBrandedType average MoneyUnit.Satoshi
                    Infrastructure.LogDebug (SPrintF1 "Average fee rate of %M sat per B" averageFeeRate.SatoshiPerByte)
                    return Some averageFeeRate
                | Some electrumFeeRate, None, Some restApiFeeRate ->
                    let average = AverageFee [decimal electrumFeeRate.FeePerK.Satoshi; decimal restApiFeeRate.FeePerK.Satoshi]
                    let averageFeeRate = ToBrandedType average MoneyUnit.Satoshi
                    Infrastructure.LogDebug (SPrintF1 "Average fee rate of %M sat per B" averageFeeRate.SatoshiPerByte)
                    return Some averageFeeRate
                | Some electrumFeeRate, Some restApiFeeRate1, Some restApiFeeRate2 ->
                    let average =
                        TrustMinimizedEstimation.AverageBetween3DiscardingOutlier
                            (decimal electrumFeeRate.FeePerK.Satoshi)
                            (decimal restApiFeeRate1.FeePerK.Satoshi)
                            (decimal restApiFeeRate2.FeePerK.Satoshi)
                    let averageFeeRate = ToBrandedType average MoneyUnit.Satoshi
                    Infrastructure.LogDebug (SPrintF1 "Average fee rate of %M sat per B" averageFeeRate.SatoshiPerByte)
                    return Some averageFeeRate
            | currency ->
                return failwith <| SPrintF1 "UTXO currency not supported yet?: %A" currency
        }

    let internal EstimateFeeRate currency (priority: Priority): Async<FeeRate> =
        async {
            let! maybeFeeRate = QueryFeeRateInternal currency priority
            match maybeFeeRate with
            | None -> return failwith "Sending when offline not supported, try sign-off?"
            | Some feeRate -> return feeRate
        }

