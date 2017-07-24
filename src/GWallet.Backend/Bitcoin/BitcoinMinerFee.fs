namespace GWallet.Backend.Bitcoin

open System
open System.Numerics

open GWallet.Backend
open NBitcoin

type MinerFee(minerFeeInSatoshis: Int64, estimationTime: DateTime, draftTransaction: Transaction) =
    member val ValueInSatoshis = minerFeeInSatoshis with get
    member val DraftTransaction = draftTransaction with get

    interface IBlockchainFee with
        member val EstimationTime = estimationTime with get

        member val Value =
            Convert.ToDecimal(minerFeeInSatoshis) / 100000000m with get

