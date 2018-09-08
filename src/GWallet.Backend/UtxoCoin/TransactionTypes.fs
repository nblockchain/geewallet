namespace GWallet.Backend.UtxoCoin

open GWallet.Backend

type OutputInfo =
    {
        ValueInSatoshis: int64;

        // FIXME: this info is already in the UnsignedTransactionProposal type, we can remove it from here:
        DestinationAddress: string;
    }

type TransactionOutpointInfo =
    {
        TransactionHash: string;
        OutputIndex: int;
        ValueInSatoshis: int64;
        DestinationInHex: string;
    }

type TransactionDraft =
    {
        Inputs: List<TransactionOutpointInfo>;
        Outputs: List<OutputInfo>;
    }

type TransactionMetadata =
    {
        Fee: MinerFee;
        TransactionDraft: TransactionDraft;
    }
    interface IBlockchainFeeInfo with
        member this.FeeEstimationTime with get() = this.Fee.EstimationTime
        member this.FeeValue
            with get() =
                this.Fee.CalculateAbsoluteValueInSatoshis() |> UnitConversion.FromSatoshiToBtc
        member this.Currency with get() = this.Fee.Currency

