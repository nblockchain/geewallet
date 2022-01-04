namespace GWallet.Backend.UtxoCoin.Lightning

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

type RecoveryTx =
    {
        ChannelId: ChannelIdentifier
        Currency: Currency
        Tx: UtxoTransaction
        Fee: MinerFee
    }
