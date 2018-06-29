namespace GWallet.Backend

open System

type CurrencyType =
    Fiat | Crypto

module Formatting =

    let DecimalAmount currencyType (amount: decimal): string =
        let amountOfDecimalsToShow =
            match currencyType with
            | CurrencyType.Fiat -> 2
            | CurrencyType.Crypto -> 5

        Math.Round(amount, amountOfDecimalsToShow)

            // line below is to add thousand separators and not show zeroes on the right...
            .ToString("#,0." + String('#', amountOfDecimalsToShow))
