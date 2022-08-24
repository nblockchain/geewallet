namespace GWallet.Backend.UtxoCoin.Lightning

open NBitcoin
open DotNetLightning.Channel.ClosingHelpers

open GWallet.Backend
open GWallet.Backend.UtxoCoin

type MutualCloseTx =
    {
        Tx: UtxoTransaction
    }

type ForceCloseTx =
    {
        Tx: UtxoTransaction
    }

type ClosingTx =
    | MutualClose of MutualCloseTx
    | ForceClose of ForceCloseTx

type MutualCloseCpfp =
    {
        ChannelId: ChannelIdentifier
        Currency: Currency
        Tx: UtxoTransaction
        Fee: MinerFee
    }

type FeeBumpTx =
    {
        ChannelId: ChannelIdentifier
        Currency: Currency
        Tx: UtxoTransaction
        Fee: MinerFee
    }

type RecoveryTx =
    {
        ChannelId: ChannelIdentifier
        Currency: Currency
        Tx: UtxoTransaction
        Fee: MinerFee
    }

type HtlcRecoveryTx =
    {
        ChannelId: ChannelIdentifier
        Currency: Currency
        HtlcTxId: TransactionIdentifier
        Tx: UtxoTransaction
        Fee: MinerFee
        AmountInSatoshis: AmountInSatoshis
    }

type HtlcTx =
    {
        ChannelId: ChannelIdentifier
        Currency: Currency
        Tx: UtxoTransaction
        NeedsRecoveryTx: bool
        Fee: MinerFee
        AmountInSatoshis: AmountInSatoshis
    }

    /// Returns true if htlc amount is less than or equal to the fees needed to spend it
    member self.IsDust () =
        if not self.NeedsRecoveryTx then
            self.AmountInSatoshis <= (uint64 self.Fee.EstimatedFeeInSatoshis)
        else
            let previousSize = self.Tx.NBitcoinTx.GetVirtualSize() |> double
            let recoveryTxWeight = 273.
            let newSize = previousSize + recoveryTxWeight
            self.AmountInSatoshis <= ((((self.Fee.EstimatedFeeInSatoshis |> double) / previousSize) * newSize) |> System.Convert.ToUInt64)

type HtlcTxsList =
    internal {
        ChannelId: ChannelIdentifier
        ClosingTxOpt: Option<Transaction>
        Currency: Currency
        Transactions: list<HtlcTransaction>
        Done: bool
    }

    member self.IsEmpty () =
        Seq.isEmpty self.Transactions || self.ClosingTxOpt.IsNone
    member self.IsDone () =
        self.Done
