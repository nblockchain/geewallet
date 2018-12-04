namespace GWallet.Backend.Tests

open NUnit.Framework

open GWallet.Backend

[<TestFixture>]
type Formatting() =

    [<Test>]
    member __.``basic fiat thousand separator test``() =
        let someUsdDecimalAmount = 1000.12m
        let formattedAmount = Formatting.DecimalAmount CurrencyType.Fiat someUsdDecimalAmount
        Assert.That(formattedAmount, Is.EqualTo "1,000.12")

    [<Test>]
    member __.``basic crypto thousand separator test``() =
        let someCryptoDecimalAmount = 1000.12m
        let formattedAmount = Formatting.DecimalAmount CurrencyType.Crypto someCryptoDecimalAmount
        Assert.That(formattedAmount, Is.EqualTo "1,000.12")

    [<Test>]
    member __.``basic fiat rounding down test``() =
        let someUsdDecimalAmount = 0.013m
        let formattedAmount = Formatting.DecimalAmount CurrencyType.Fiat someUsdDecimalAmount
        Assert.That(formattedAmount, Is.EqualTo "0.01")

    [<Test>]
    member __.``basic fiat rounding up test``() =
        let someUsdDecimalAmount = 0.016m
        let formattedAmount = Formatting.DecimalAmount CurrencyType.Fiat someUsdDecimalAmount
        Assert.That(formattedAmount, Is.EqualTo "0.02")

    [<Test>]
    member __.``basic crypto rounding down test``() =
        let someCryptoDecimalAmount = 0.000012m
        let formattedAmount = Formatting.DecimalAmount CurrencyType.Crypto someCryptoDecimalAmount
        Assert.That(formattedAmount, Is.EqualTo "0.00001")

    [<Test>]
    member __.``basic crypto rounding up test``() =
        let someCryptoDecimalAmount = 0.000016m
        let formattedAmount = Formatting.DecimalAmount CurrencyType.Crypto someCryptoDecimalAmount
        Assert.That(formattedAmount, Is.EqualTo "0.00002")

    [<Test>]
    [<Ignore("FIXME, not working yet")>]
    member __.``if it's not zero, even if super tiny, it shouldn't round to zero!``() =
        let someVerySmallUsdDecimalAmount = 0.0000001m
        let formattedAmount = Formatting.DecimalAmount CurrencyType.Fiat someVerySmallUsdDecimalAmount
        Assert.That(formattedAmount, Is.EqualTo "0.01")

    [<Test>]
    member __.``trailing zeros always with fiat``() =
        let someUsdDecimalAmount1 = 2m
        let formattedAmount1 = Formatting.DecimalAmount CurrencyType.Fiat someUsdDecimalAmount1
        Assert.That(formattedAmount1, Is.EqualTo "2.00")

        let someUsdDecimalAmount2 = 2.1m
        let formattedAmount2 = Formatting.DecimalAmount CurrencyType.Fiat someUsdDecimalAmount2
        Assert.That(formattedAmount2, Is.EqualTo "2.10")

    [<Test>]
    member __.``no trailing zeros with crypto``() =
        let someCryptoDecimalAmount1 = 2m
        let formattedAmount1 = Formatting.DecimalAmount CurrencyType.Crypto someCryptoDecimalAmount1
        Assert.That(formattedAmount1, Is.EqualTo "2")

        let someCryptoDecimalAmount2 = 2.1m
        let formattedAmount2 = Formatting.DecimalAmount CurrencyType.Crypto someCryptoDecimalAmount2
        Assert.That(formattedAmount2, Is.EqualTo "2.1")

    [<Test>]
    member __.``varying number of decimals in crypto case: 5 decimals if less than 1``() =
        let someCryptoDecimalAmount1 = 0.123456m
        let formattedAmount1 = Formatting.DecimalAmount CurrencyType.Crypto someCryptoDecimalAmount1
        Assert.That(formattedAmount1, Is.EqualTo "0.12346")

    [<Test>]
    member __.``varying number of decimals in crypto case: 4 decimals if within [1,10) range``() =
        let someCryptoDecimalAmount1 = 1.123456m
        let formattedAmount1 = Formatting.DecimalAmount CurrencyType.Crypto someCryptoDecimalAmount1
        Assert.That(formattedAmount1, Is.EqualTo "1.1235")

    [<Test>]
    member __.``varying number of decimals in crypto case: 3 decimals if within [10,100) range``() =
        let someCryptoDecimalAmount1 = 12.123456m
        let formattedAmount1 = Formatting.DecimalAmount CurrencyType.Crypto someCryptoDecimalAmount1
        Assert.That(formattedAmount1, Is.EqualTo "12.123")

    [<Test>]
    member __.``varying number of decimals in crypto case: 2 decimals if >100``() =
        let someCryptoDecimalAmount1 = 123.123456m
        let formattedAmount1 = Formatting.DecimalAmount CurrencyType.Crypto someCryptoDecimalAmount1
        Assert.That(formattedAmount1, Is.EqualTo "123.12")

        let someCryptoDecimalAmount1 = 1234.123456m
        let formattedAmount1 = Formatting.DecimalAmount CurrencyType.Crypto someCryptoDecimalAmount1
        Assert.That(formattedAmount1, Is.EqualTo "1,234.12")

