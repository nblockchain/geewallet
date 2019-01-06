namespace GWallet.Backend.Ether

open GWallet.Backend

type TransactionMetadata =
    {
        Fee: MinerFee;

        // this below cannot be directly BigInteger because it needs to be JSON-serialized later
        TransactionCount: int64;
    }
    interface IBlockchainFeeInfo with
        member self.FeeEstimationTime with get() = self.Fee.EstimationTime
        member self.FeeValue with get() = self.Fee.CalculateAbsoluteValue()
        member self.Currency with get() = self.Fee.Currency
