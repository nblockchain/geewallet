namespace GWallet.Backend.Tests

open NUnit.Framework

open GWallet.Backend

[<TestFixture>]
type MetaMarshalling() =

    [<Test>]
    member __.``wrapper's TypeName property doesn't contain assembly-qualified-name``() =
        let json = Marshalling.Serialize MarshallingData.SignedBtcTransactionExample
        Assert.That(json, Is.Not.Null)
        Assert.That(json, Is.Not.Empty)

        let wrapperTypeString = Marshalling.ExtractStringType json
        Assert.That(wrapperTypeString, Is.Not.Null)
        Assert.That(wrapperTypeString, Is.Not.Empty)

        Assert.That(wrapperTypeString, Does.Not.Contain "Version=")
