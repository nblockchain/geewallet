namespace GWallet.Backend.Ether

open GWallet.Backend

type TransactionMetadata =
    {
        Fee: MinerFee;

        // this below cannot be directly BigInteger because it needs to be JSON-serialized later
        TransactionCount: int64;
    }
    interface IBlockchainFeeInfo with
        member this.FeeEstimationTime with get() = this.Fee.EstimationTime
        member this.FeeValue with get() = this.Fee.CalculateAbsoluteValue()