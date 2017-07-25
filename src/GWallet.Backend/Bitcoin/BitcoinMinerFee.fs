namespace GWallet.Backend.Bitcoin

open System
open System.Numerics

open GWallet.Backend
open NBitcoin

type MinerFee(estimatedTransactionSizeInBytes: int, btcPerKiloByteForFastTrans: decimal,
              estimationTime: DateTime, draftTransaction: Transaction) =

    member val DraftTransaction = draftTransaction with get
    member val EstimatedTransactionSizeInBytes = estimatedTransactionSizeInBytes with get

    interface IBlockchainFee with
        member val EstimationTime = estimationTime with get

        member val Value =
            let satPerByteForFastTrans = btcPerKiloByteForFastTrans * 100000000m / 1024m
            let totalFeeForThisTransInSatoshis = satPerByteForFastTrans * decimal estimatedTransactionSizeInBytes
            let totalFeeInSatoshisRemovingDecimals = Convert.ToInt64 totalFeeForThisTransInSatoshis
            Convert.ToDecimal(totalFeeInSatoshisRemovingDecimals) / 100000000m with get

