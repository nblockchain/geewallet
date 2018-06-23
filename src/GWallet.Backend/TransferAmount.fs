namespace GWallet.Backend

open System

type TransferAmount(valueToSend: decimal, balanceAtTheMomentOfSending: decimal, currency: Currency) =
    do
        if valueToSend <= 0m then
            invalidArg "valueToSend" "Amount has to be above zero"
        if balanceAtTheMomentOfSending < valueToSend then
            invalidArg "balanceAtTheMomentOfSending" "balance has to be equal or higher than valueToSend"

    member this.ValueToSend
        with get() = Math.Round(valueToSend, currency.DecimalPlaces())

    member this.BalanceAtTheMomentOfSending
        with get() = balanceAtTheMomentOfSending

    member this.Currency
        with get() = currency

    member this.TotalValueIncludingFeeIfSameCurrency (feeInfo: IBlockchainFeeInfo) =
        if valueToSend = balanceAtTheMomentOfSending || currency <> feeInfo.Currency then
            this.ValueToSend
        else
            feeInfo.FeeValue + this.ValueToSend
