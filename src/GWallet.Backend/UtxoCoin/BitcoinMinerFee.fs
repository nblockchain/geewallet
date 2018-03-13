namespace GWallet.Backend.UtxoCoin

open System

open GWallet.Backend

type MinerFee(estimatedTransactionSizeInBytes: int,
              amountPerKiloByteForFastTransaction: decimal,
              estimationTime: DateTime,
              currency: Currency) =

    member val EstimatedTransactionSizeInBytes = estimatedTransactionSizeInBytes with get
    member val AmountPerKiloByteForFastTransaction = amountPerKiloByteForFastTransaction with get

    member val EstimationTime = estimationTime with get

    member val Currency = currency with get

    member this.CalculateAbsoluteValue() =
        let satPerByteForFastTrans = amountPerKiloByteForFastTransaction * 100000000m / 1024m
        let totalFeeForThisTransInSatoshis = satPerByteForFastTrans * decimal estimatedTransactionSizeInBytes
        let totalFeeInSatoshisRemovingDecimals = Convert.ToInt64 totalFeeForThisTransInSatoshis
        UnitConversion.FromSatoshiToBtc totalFeeInSatoshisRemovingDecimals
