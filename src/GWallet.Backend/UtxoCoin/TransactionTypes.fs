﻿namespace GWallet.Backend.UtxoCoin

open GWallet.Backend

open NBitcoin

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
                (Money.Satoshis self.Fee.EstimatedFeeInSatoshis).ToUnit MoneyUnit.BTC
        member self.Currency with get() = self.Fee.Currency

