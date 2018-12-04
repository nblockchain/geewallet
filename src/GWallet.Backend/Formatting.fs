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
                let numberOfDecimals =
                    if amount < 1m then
                        5
                    elif amount < 10m then
                        4
                    elif amount < 100m then
                        3
                    else
                        2
                numberOfDecimals,sprintf "#,0.%s" (String('#', numberOfDecimals))

        Math.Round(amount, amountOfDecimalsToShow)
            .ToString formattingStrategy
