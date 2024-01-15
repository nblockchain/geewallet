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

        let wrapper = Marshalling.ExtractWrapper json
        Assert.That(wrapper, Is.Not.Null)
        Assert.That(wrapper.TypeName, Is.Not.Null)
        Assert.That(wrapper.TypeName, Is.Not.Empty)
        Assert.That(wrapper.Version, Is.Not.Null)
        Assert.That(wrapper.Version, Is.Not.Empty)

        Assert.That(wrapper.TypeName, Does.Not.Contain "Version=")
