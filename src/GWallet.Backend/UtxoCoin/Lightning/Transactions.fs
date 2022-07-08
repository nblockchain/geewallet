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

type HtlcTx =
    {
        ChannelId: ChannelIdentifier
        Currency: Currency
        Tx: UtxoTransaction
        Fee: MinerFee
    }

type HtlcTxsList =
    {
        ChannelId: ChannelIdentifier
        ClosingTx: Transaction
        Currency: Currency
        Transactions: list<HtlcTransaction>
        Done: bool
    }

    member self.IsEmpty () =
        Seq.isEmpty self.Transactions