namespace GWallet.Backend

open System

type TransferAmount(valueToSend: UnsignedDecimal, balanceAtTheMomentOfSending: UnsignedDecimal, currency: Currency) =
    do
        if balanceAtTheMomentOfSending.Value < valueToSend.Value then
            invalidArg "balanceAtTheMomentOfSending" "balance has to be equal or higher than valueToSend"

    member this.ValueToSend
        with get() = Math.Round(valueToSend.Value, currency.DecimalPlaces()) |> UnsignedDecimal

    member this.BalanceAtTheMomentOfSending
        with get() = balanceAtTheMomentOfSending

    member this.Currency
        with get() = currency

