namespace GWallet.Backend.UtxoCoin.Lightning.Watcher

type AddPunishmentTxRequest =
    {
        TransactionHex: string
        CommitmentTxHash: string
    }
