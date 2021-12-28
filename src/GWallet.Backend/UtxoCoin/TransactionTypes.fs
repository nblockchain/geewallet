namespace GWallet.Backend.UtxoCoin

open GWallet.Backend

open NBitcoin

type TransactionInputOutpointInfo =
    {
        TransactionHash: string;
        OutputIndex: int;
        ValueInSatoshis: int64;
        DestinationInHex: string;
    }

// FIXME: now that MinerFee implements IBlockchainInfo, no need to use TxMetadata in many places
type TransactionMetadata =
    {
        Fee: MinerFee;
        Inputs: List<TransactionInputOutpointInfo>;
    }
    interface IBlockchainFeeInfo with
        member self.FeeEstimationTime = (self.Fee :> IBlockchainFeeInfo).FeeEstimationTime
        member self.FeeValue = (self.Fee :> IBlockchainFeeInfo).FeeValue
        member self.Currency = (self.Fee :> IBlockchainFeeInfo).Currency

