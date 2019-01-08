namespace GWallet.Backend.UtxoCoin

open GWallet.Backend

type TransactionInputOutpointInfo =
    {
        TransactionHash: string;
        OutputIndex: int;
        ValueInSatoshis: int64;
        DestinationInHex: string;
    }

type TransactionMetadata =
    {
        Fee: MinerFee;
        Inputs: List<TransactionInputOutpointInfo>;
    }
    interface IBlockchainFeeInfo with
        member self.FeeEstimationTime with get() = self.Fee.EstimationTime
        member self.FeeValue
            with get() =
                self.Fee.EstimatedFeeInSatoshis |> UnitConversion.FromSatoshiToBtc
        member self.Currency with get() = self.Fee.Currency

