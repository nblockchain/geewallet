namespace GWallet.Backend

open System

type CurrencyType =
    Fiat | Crypto

module Formatting =

    // with this we want to avoid the weird default US format of starting with the month, then day, then year... sigh
    let ShowSaneDate (date: DateTime): string =
        date.ToString "dd-MMM-yyyy"

    let DecimalAmountRounding currencyType (amount: decimal): string =
        let amountOfDecimalsToShow,formattingStrategy =
            match currencyType with
            | CurrencyType.Fiat ->
                let twoDecimals = 2
                twoDecimals,sprintf "N%d" twoDecimals
            | CurrencyType.Crypto ->
                let fiveDecimals = 5
                fiveDecimals,sprintf "#,0.%s" (String('#', fiveDecimals))

        Math.Round(amount, amountOfDecimalsToShow)
            .ToString formattingStrategy

    let DecimalAmountTruncating (currencyType: CurrencyType) (amount: decimal) (maxAmount: decimal)
                                    : string =
        let amountOfDecimalsToShow =
            match currencyType with
            | CurrencyType.Fiat -> 2
            | CurrencyType.Crypto -> 5
        // https://stackoverflow.com/a/25451689/544947
        let truncated = amount - (amount % (1m / decimal(Math.Pow(10., float amountOfDecimalsToShow))))
        if (truncated > maxAmount) then
            failwithf "how can %s be higher than %s?" (truncated.ToString()) (maxAmount.ToString())

        DecimalAmountRounding currencyType truncated
