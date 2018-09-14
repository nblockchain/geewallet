namespace GWallet.Backend

open System

type IBlockchainFeeInfo =
    abstract member FeeEstimationTime: DateTime with get
    abstract member FeeValue: UnsignedDecimal with get
    abstract member Currency: Currency with get

