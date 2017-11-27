namespace GWallet.Backend.Bitcoin

open System

open GWallet.Backend

type MinerFee(estimatedTransactionSizeInBytes: int, btcPerKiloByteForFastTrans: decimal,
              estimationTime: DateTime, draftTransaction: TransactionDraft) =

    member val DraftTransaction = draftTransaction with get
    member val EstimatedTransactionSizeInBytes = estimatedTransactionSizeInBytes with get

    // FIXME: how to not repeat properties but still have them serialized
    // as part of the public interface?? :(
    member val EstimationTime = estimationTime with get

    interface IBlockchainFee with
        member val EstimationTime = estimationTime with get

        member val Value =
            let satPerByteForFastTrans = btcPerKiloByteForFastTrans * 100000000m / 1024m
            let totalFeeForThisTransInSatoshis = satPerByteForFastTrans * decimal estimatedTransactionSizeInBytes
            let totalFeeInSatoshisRemovingDecimals = Convert.ToInt64 totalFeeForThisTransInSatoshis
            Convert.ToDecimal(totalFeeInSatoshisRemovingDecimals) / 100000000m with get

