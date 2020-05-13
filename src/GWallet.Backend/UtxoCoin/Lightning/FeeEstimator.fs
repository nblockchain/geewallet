namespace GWallet.Backend.UtxoCoin.Lightning

open NBitcoin

open DotNetLightning.Utils
open DotNetLightning.Chain

open GWallet.Backend
open GWallet.Backend.UtxoCoin

type FeeEstimator = {
    FeeRatePerKw: FeeRatePerKw
} with
    static member Create() = async {
        let averageFee (feesFromDifferentServers: List<decimal>): decimal =
            let sum: decimal = List.sum feesFromDifferentServers
            let avg = sum / decimal feesFromDifferentServers.Length
            avg
        let estimateFeeJob = ElectrumClient.EstimateFee 2 // same confirmation target as in UtxoCoinAccount
        let! btcPerKb = Server.Query Currency.BTC (QuerySettings.FeeEstimation averageFee) estimateFeeJob None
        let satPerKb = (Money (btcPerKb, MoneyUnit.BTC)).ToUnit MoneyUnit.Satoshi
        // 4 weight units per byte. See segwit specs.
        let kwPerKb = 4m
        let satPerKw = satPerKb / kwPerKb
        let feeRatePerKw = FeeRatePerKw (uint32 satPerKw)
        return { FeeRatePerKw = feeRatePerKw }
    }

    interface IFeeEstimator with
        member this.GetEstSatPer1000Weight(_: ConfirmationTarget) =
            this.FeeRatePerKw
