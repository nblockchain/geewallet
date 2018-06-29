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

            // FIXME: the below returns "0" when the decimal is too low, e.g. for non crypto 0.001 USD -> 0
            //                                                  (in this case maybe we should round up to 0.01)

            // line below is to add thousand separators and not show zeroes on the right...
            .ToString("#,0." + String('#', amountOfDecimalsToShow))
