namespace GWallet.Backend.UtxoCoin

open System

open GWallet.Backend

//FIXME: convert to record?
type MinerFee(estimatedFeeInSatoshis: int64,
              estimationTime: DateTime,
              currency: Currency) =

    member val EstimatedFeeInSatoshis = estimatedFeeInSatoshis with get

    member val EstimationTime = estimationTime with get

    member val Currency = currency with get

