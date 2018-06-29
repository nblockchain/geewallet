namespace GWallet.Backend

open System

type CurrencyType =
    Fiat | Crypto

module Formatting =

    let DecimalAmount currencyType (amount: decimal): string =
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
