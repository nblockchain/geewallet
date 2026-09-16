namespace GWallet.Backend.Tests

open System
open System.Runtime.Serialization

open NUnit.Framework

open GWallet.Backend

// BinaryFormatter has been removed from .NET10, we might as well just stop testing
// exception marshalling altogether in any .NET version (it's a feature we removed)
[<TestFixture>]
type ExceptionMarshalling () =

    let SerializeBasicException () =
        let ex = Exception "msg"
        Marshalling.Serialize ex

    [<Test>]
    member __.``can serialize basic exceptions``() =
        Assert.Throws<NotSupportedException>(
            fun _ -> SerializeBasicException() |> ignore<string>
        ) |> ignore<Exception>

    [<Test>]
    member __.``can deserialize basic exceptions``() =
        Assert.Throws<NotSupportedException>(
            fun _ ->
                Marshalling.Deserialize MarshallingData.BasicExceptionExampleInJson
                |> ignore<Exception>
        ) |> ignore<Exception>

