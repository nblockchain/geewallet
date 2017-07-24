namespace GWallet.Backend

open System

type IBlockchainFee =
    abstract member EstimationTime: DateTime with get
    abstract member Value: decimal with get