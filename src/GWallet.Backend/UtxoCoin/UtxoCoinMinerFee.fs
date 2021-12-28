namespace GWallet.Backend.UtxoCoin

open System

open NBitcoin

open GWallet.Backend

//FIXME: convert to record?
type MinerFee(estimatedFeeInSatoshis: int64,
              estimationTime: DateTime,
              currency: Currency) =

    member val EstimatedFeeInSatoshis = estimatedFeeInSatoshis with get

    member val EstimationTime = estimationTime with get

    member val Currency = currency with get

    interface IBlockchainFeeInfo with
        member self.FeeEstimationTime = self.EstimationTime
        member self.FeeValue =
            (Money.Satoshis self.EstimatedFeeInSatoshis).ToUnit MoneyUnit.BTC
        member self.Currency = self.Currency
