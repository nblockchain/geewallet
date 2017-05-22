namespace GWallet.Frontend.Console

open System

open System.Text.RegularExpressions

type CurrencyType =
    Fiat | Crypto

module Presentation =

    // with this we want to avoid the weird default US format of starting with the month, then day, then year... sigh
    let ShowSaneDate (date: DateTime): string =
        date.ToString("dd-MMM-yyyy")

    let ShowDecimalForHumans currencyType (amount: decimal): string =
        let amountOfDecimalsToShow =
            match currencyType with
            | CurrencyType.Fiat -> 2
            | CurrencyType.Crypto -> 5

        // line below is to add thousand separators
        Math.Round(amount, amountOfDecimalsToShow)
            .ToString("N" + amountOfDecimalsToShow.ToString())

    let ConvertPascalCaseToSentence(pascalCaseElement: string) =
        Regex.Replace(pascalCaseElement, "[a-z][A-Z]",
                      (fun (m: Match) -> m.Value.[0].ToString() + " " + Char.ToLower(m.Value.[1]).ToString()))
