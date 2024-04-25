namespace GWallet.Backend.UtxoCoin

open System
open System.Linq

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks

open NBitcoin

module FeeRateEstimation =

    let private QueryFeeRateToElectrumServers (currency: Currency): Async<decimal> =
        async {
            let averageFee (feesFromDifferentServers: List<decimal>): decimal =
                let avg = feesFromDifferentServers.Sum() / decimal feesFromDifferentServers.Length
                avg

            //querying for 1 will always return -1 surprisingly...
            let estimateFeeJob = ElectrumClient.EstimateFee 2
            let! btcPerKiloByteForFastTrans =
                Server.Query currency (QuerySettings.FeeEstimation averageFee) estimateFeeJob None
            return btcPerKiloByteForFastTrans
        }

    let internal EstimateFeeRate currency: Async<FeeRate> =
        let toBrandedType(feeRate: decimal): FeeRate =
            try
                Money(feeRate, MoneyUnit.BTC) |> FeeRate
            with
            | ex ->
                // we need more info in case this bug shows again: https://gitlab.com/nblockchain/geewallet/issues/43
                raise <| Exception(SPrintF1 "Could not create fee rate from %s btc per KB"
                                            (feeRate.ToString()), ex)
        async {
            let! btcPerKiloByteForFastTrans = QueryFeeRateToElectrumServers currency
            return toBrandedType btcPerKiloByteForFastTrans
        }

