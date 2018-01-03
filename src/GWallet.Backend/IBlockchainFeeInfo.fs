namespace GWallet.Backend

open System

type IBlockchainFeeInfo =
    abstract member FeeEstimationTime: DateTime with get
    abstract member FeeValue: decimal with get