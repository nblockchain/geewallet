namespace GWallet.Backend.UtxoCoin

open GWallet.Backend

type OutputInfo =
    {
        ValueInSatoshis: int64;

        // FIXME: this info is already in the UnsignedTransactionProposal type, we can remove it from here:
        DestinationAddress: string;
    }

type RawTransactionOutpoint =
    {
        RawTransaction: string;
        OutputIndex: int;
    }

type TransactionDraft =
    {
        Inputs: list<RawTransactionOutpoint>;
        Outputs: list<OutputInfo>;
    }

type TransactionMetadata =
    {
        Fee: MinerFee;
        TransactionDraft: TransactionDraft;
    }
    interface IBlockchainFeeInfo with
        member this.FeeEstimationTime with get() = this.Fee.EstimationTime
        member this.FeeValue with get() = this.Fee.CalculateAbsoluteValue()