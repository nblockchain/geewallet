namespace GWallet.Backend.UtxoCoin

open System
open System.Linq

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks

open FSharp.Data
open NBitcoin

module FeeRateEstimation =

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

    let private ToBrandedType(feeRatePerKB: decimal) (moneyUnit: MoneyUnit): FeeRate =
        try
            Money(feeRatePerKB, moneyUnit) |> FeeRate
        with
        | ex ->
            // we need more info in case this bug shows again: https://gitlab.com/nblockchain/geewallet/issues/43
            raise <| Exception(SPrintF2 "Could not create fee rate from %s %A"
                                        (feeRatePerKB.ToString()) moneyUnit, ex)

    let private QueryFeeRateToMempoolSpace (): Async<Option<FeeRate>> =
        async {
            let! maybeJson = Networking.QueryRestApi MempoolSpaceRestApiUri
            match maybeJson with
            | None -> return None
            | Some json ->
                let recommendedFees = MempoolSpaceProvider.Parse json
                let highPrioFeeSatsPerB = decimal recommendedFees.FastestFee
                Infrastructure.LogDebug (SPrintF1 "mempool.space API gave us a fee rate of %M sat per B" highPrioFeeSatsPerB)
                let satPerKB = highPrioFeeSatsPerB * (decimal 1000)
                return Some <| ToBrandedType satPerKB MoneyUnit.Satoshi
        }

    let private AverageFee (feesFromDifferentServers: List<decimal>): decimal =
        let avg = feesFromDifferentServers.Sum() / decimal feesFromDifferentServers.Length
        avg

    let private QueryFeeRateToElectrumServers (currency: Currency): Async<FeeRate> =
        async {
            //querying for 1 will always return -1 surprisingly...
            let numBlocksToWait = 2
            let estimateFeeJob = ElectrumClient.EstimateFee numBlocksToWait
            let! btcPerKiloByteForFastTrans =
                Server.Query currency (QuerySettings.FeeEstimation AverageFee) estimateFeeJob None
            return ToBrandedType (decimal btcPerKiloByteForFastTrans) MoneyUnit.BTC
        }

    let QueryFeeRateInternal currency =
        let electrumJob =
            async {
                try
                    let! result = QueryFeeRateToElectrumServers currency
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
                let! bothJobs = Async.Parallel [electrumJob; QueryFeeRateToMempoolSpace()]
                let electrumResult = bothJobs.ElementAt 0
                let mempoolSpaceResult = bothJobs.ElementAt 1
                match electrumResult, mempoolSpaceResult with
                | None, None -> return None
                | Some feeRate, None ->
                    Infrastructure.LogDebug "Only electrum servers available for feeRate estimation"
                    return Some feeRate
                | None, Some feeRate ->
                    Infrastructure.LogDebug "Only mempool.space API available for feeRate estimation"
                    return Some feeRate
                | Some electrumFeeRate, Some mempoolSpaceFeeRate ->
                    let average = AverageFee [decimal electrumFeeRate.FeePerK.Satoshi; decimal mempoolSpaceFeeRate.FeePerK.Satoshi]
                    let averageFeeRate = ToBrandedType average MoneyUnit.Satoshi
                    Infrastructure.LogDebug (SPrintF1 "Average fee rate of %M sat per B" averageFeeRate.SatoshiPerByte)
                    return Some averageFeeRate
            | currency ->
                return failwith <| SPrintF1 "UTXO currency not supported yet?: %A" currency
        }

    let internal EstimateFeeRate currency: Async<FeeRate> =
        async {
            let! maybeFeeRate = QueryFeeRateInternal currency
            match maybeFeeRate with
            | None -> return failwith "Sending when offline not supported, try sign-off?"
            | Some feeRate -> return feeRate
        }

