namespace GWallet.Backend.Ether

open GWallet.Backend

// FIXME: now that MinerFee implements IBlockchainInfo, no need to use TxMetadata in many places
type TransactionMetadata =
    {
        Fee: MinerFee;

        // this below cannot be directly BigInteger because it needs to be JSON-serialized later
        TransactionCount: int64;
    }
    interface IBlockchainFeeInfo with
        member self.FeeEstimationTime = (self.Fee :> IBlockchainFeeInfo).FeeEstimationTime
        member self.FeeValue = (self.Fee :> IBlockchainFeeInfo).FeeValue
        member self.Currency = (self.Fee :> IBlockchainFeeInfo).Currency
