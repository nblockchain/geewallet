namespace GWallet.Backend.Tests

open NUnit.Framework

open GWallet.Backend

module Formatting =

    [<Test>]
    let ``basic fiat thousand separator test``() =
        let someUsdDecimalAmount = 1000m
        let formattedAmount = Formatting.DecimalAmount CurrencyType.Fiat someUsdDecimalAmount
        Assert.That(formattedAmount, Is.EqualTo "1,000")

    [<Test>]
    let ``basic crypto thousand separator test``() =
        let someCryptoDecimalAmount = 1000m
        let formattedAmount = Formatting.DecimalAmount CurrencyType.Crypto someCryptoDecimalAmount
        Assert.That(formattedAmount, Is.EqualTo "1,000")

    [<Test>]
    let ``basic fiat rounding down test``() =
        let someUsdDecimalAmount = 0.013m
        let formattedAmount = Formatting.DecimalAmount CurrencyType.Fiat someUsdDecimalAmount
        Assert.That(formattedAmount, Is.EqualTo "0.01")

    [<Test>]
    let ``basic fiat rounding up test``() =
        let someUsdDecimalAmount = 0.016m
        let formattedAmount = Formatting.DecimalAmount CurrencyType.Fiat someUsdDecimalAmount
        Assert.That(formattedAmount, Is.EqualTo "0.02")

    [<Test>]
    let ``basic crypto rounding down test``() =
        let someCryptoDecimalAmount = 0.000012m
        let formattedAmount = Formatting.DecimalAmount CurrencyType.Crypto someCryptoDecimalAmount
        Assert.That(formattedAmount, Is.EqualTo "0.00001")

    [<Test>]
    let ``basic crypto rounding up test``() =
        let someCryptoDecimalAmount = 0.000016m
        let formattedAmount = Formatting.DecimalAmount CurrencyType.Crypto someCryptoDecimalAmount
        Assert.That(formattedAmount, Is.EqualTo "0.00002")

    [<Test>]
    [<Ignore("FIXME, not working yet")>]
    let ``if it's not zero, even if super tiny, it shouldn't round to zero!``() =
        let someVerySmallUsdDecimalAmount = 0.0000001m
        let formattedAmount = Formatting.DecimalAmount CurrencyType.Fiat someVerySmallUsdDecimalAmount
        Assert.That(formattedAmount, Is.EqualTo "0.01")
