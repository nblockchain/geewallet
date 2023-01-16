namespace GWallet.Backend.Tests

open System
open System.Threading.Tasks

open NUnit.Framework

open GWallet.Backend


type TypeWithStringOverridenManually =
    | FOO
    | BAR
    override self.ToString() =
        match self with
        | FOO -> "FOO"
        | BAR -> "BAR"

type TypeWithNoToStringOverriden =
    | FOO
    | BAR

module IsString =
    let WhichDoesNotEndWith (str: string) =
#if !LEGACY_FRAMEWORK
        Does.Not.EndWith str
#else
        Is.Not.StringEnding str
#endif

    let WhichContains (str: string) =
#if !LEGACY_FRAMEWORK
        Does.Contain str
#else
        Is.StringContaining str
#endif

    let StartingWith (str: string) =
#if !LEGACY_FRAMEWORK
        Does.StartWith str
#else
        Is.StringStarting str
#endif

[<TestFixture>]
type FSharpUtilCoverage() =

    [<Test>]
    member __.``converts fsharp's print syntax to String-Format (basic)``() =
        let basicStr = "%s"
        Assert.That(FSharpUtil.ReflectionlessPrint.ToStringFormat basicStr, Is.EqualTo "{0}")
        Assert.That(FSharpUtil.ReflectionlessPrint.SPrintF1 basicStr "foo", Is.EqualTo "foo")

        let basicInt1 = "%i"
        Assert.That(FSharpUtil.ReflectionlessPrint.ToStringFormat basicInt1, Is.EqualTo "{0}")
        Assert.That(FSharpUtil.ReflectionlessPrint.SPrintF1 basicInt1 1, Is.EqualTo "1")

        let basicInt2 = "%d"
        Assert.That(FSharpUtil.ReflectionlessPrint.ToStringFormat basicInt2, Is.EqualTo "{0}")
        Assert.That(FSharpUtil.ReflectionlessPrint.SPrintF1 basicInt2 2, Is.EqualTo "2")

        let moreChars = "[%s]"
        Assert.That(FSharpUtil.ReflectionlessPrint.ToStringFormat moreChars, Is.EqualTo "[{0}]")
        Assert.That(FSharpUtil.ReflectionlessPrint.SPrintF1 moreChars "foo", Is.EqualTo "[foo]")

        let twoStrings = "%s-%s"
        Assert.That(FSharpUtil.ReflectionlessPrint.ToStringFormat twoStrings, Is.EqualTo "{0}-{1}")
        Assert.That(FSharpUtil.ReflectionlessPrint.SPrintF2 twoStrings "foo" "bar", Is.EqualTo "foo-bar")

        let twoElementsWithDifferentTypes = "%s-%i"
        Assert.That(FSharpUtil.ReflectionlessPrint.ToStringFormat twoElementsWithDifferentTypes, Is.EqualTo "{0}-{1}")
        Assert.That(FSharpUtil.ReflectionlessPrint.SPrintF2 twoElementsWithDifferentTypes "foo" 1,
                    Is.EqualTo "foo-1")

        let twoElementsWithDifferentTypesWithInverseOrder = "%i-%s"
        Assert.That(FSharpUtil.ReflectionlessPrint.ToStringFormat twoElementsWithDifferentTypesWithInverseOrder,
                    Is.EqualTo "{0}-{1}")
        Assert.That(FSharpUtil.ReflectionlessPrint.SPrintF2 twoElementsWithDifferentTypesWithInverseOrder 1 "foo",
                    Is.EqualTo "1-foo")

        let advancedEscaping = "%f%% done"
        Assert.That(FSharpUtil.ReflectionlessPrint.ToStringFormat advancedEscaping, Is.EqualTo "{0}% done")
        Assert.That(FSharpUtil.ReflectionlessPrint.SPrintF1 advancedEscaping 0.1, Is.EqualTo "0.1% done")

    [<Test>]
    member __.``converts fsharp's print syntax to String-Format (advanced I)``() =
        let advanced = "%A"
        Assert.That(FSharpUtil.ReflectionlessPrint.ToStringFormat advanced, Is.EqualTo "{0}")
        Assert.That(FSharpUtil.ReflectionlessPrint.SPrintF1 advanced TypeWithStringOverridenManually.FOO,
                    Is.EqualTo "FOO")

        let advanced2 = "%Ax%A"
        Assert.That(FSharpUtil.ReflectionlessPrint.ToStringFormat advanced2, Is.EqualTo "{0}x{1}")
        Assert.That(FSharpUtil.ReflectionlessPrint.SPrintF2 advanced2
                                                            TypeWithStringOverridenManually.FOO
                                                            TypeWithStringOverridenManually.BAR,
                    Is.EqualTo "FOOxBAR")

    [<Test>]
    [<Ignore "NOTE: this test fails with old F# versions (stockmono, stockoldmono CI lanes), passes with new versions (newmono lane)">]
    member __.``converts fsharp's print syntax to String-Format (advanced II)``() =
        let advanced = "%A"
        Assert.That(FSharpUtil.ReflectionlessPrint.ToStringFormat advanced, Is.EqualTo "{0}")
        Assert.That(FSharpUtil.ReflectionlessPrint.SPrintF1 advanced TypeWithNoToStringOverriden.FOO,
                    Is.EqualTo "FOO")

        let advanced2 = "%Ax%A"
        Assert.That(FSharpUtil.ReflectionlessPrint.ToStringFormat advanced2, Is.EqualTo "{0}x{1}")
        Assert.That(FSharpUtil.ReflectionlessPrint.SPrintF2 advanced2
                                                            TypeWithNoToStringOverriden.FOO
                                                            TypeWithNoToStringOverriden.BAR,
                    Is.EqualTo "FOOxBAR")

