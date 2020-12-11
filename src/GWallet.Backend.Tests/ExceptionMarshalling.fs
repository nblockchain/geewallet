namespace GWallet.Backend.Tests

open System

open NUnit.Framework

open GWallet.Backend

type CustomException =
   inherit Exception

   new(message: string, innerException: CustomException) =
       { inherit Exception(message, innerException) }
   new(message) =
       { inherit Exception(message) }

[<TestFixture>]
type ExceptionMarshalling () =

    [<Test>]
    member __.``can serialize basic exceptions``() =
        let ex = Exception "msg"
        let json = Marshalling.Serialize ex
        Assert.That(json, Is.Not.Null)
        Assert.That(json, Is.Not.Empty)
        Assert.That(json |> MarshallingData.Sanitize,
                    Is.EqualTo MarshallingData.BasicExceptionExampleInJson)

    [<Test>]
    member __.``can serialize real exceptions``() =
        let someEx = Exception "msg"
        let ex =
            try
                raise someEx
                someEx
            with
            | ex ->
                ex
        let json = Marshalling.Serialize ex
        Assert.That(json, Is.Not.Null)
        Assert.That(json, Is.Not.Empty)
        Assert.That(json |> MarshallingData.Sanitize,
                    Is.EqualTo MarshallingData.RealExceptionExampleInJson)

    [<Test>]
    member __.``can serialize inner exceptions``() =
        let ex = Exception("msg", Exception "innerMsg")
        let json = Marshalling.Serialize ex
        Assert.That(json, Is.Not.Null)
        Assert.That(json, Is.Not.Empty)
        Assert.That(json |> MarshallingData.Sanitize,
                    Is.EqualTo MarshallingData.InnerExceptionExampleInJson)

    [<Test>]
    member __.``can serialize custom exceptions``() =
        let ex = CustomException "msg"
        let json = Marshalling.Serialize ex
        Assert.That(json, Is.Not.Null)
        Assert.That(json, Is.Not.Empty)
        Assert.That(json |> MarshallingData.Sanitize,
                    Is.EqualTo MarshallingData.CustomExceptionExampleInJson)

    [<Test>]
    member __.``can serialize full exceptions (all previous features combined)``() =
        let someCEx = CustomException("msg", CustomException "innerMsg")
        let ex =
            try
                raise someCEx
                someCEx
            with
            | :? CustomException as cex ->
                cex
        let json = Marshalling.Serialize ex
        Assert.That(json, Is.Not.Null)
        Assert.That(json, Is.Not.Empty)
        Assert.That(json |> MarshallingData.Sanitize,
                    Is.EqualTo MarshallingData.FullExceptionExampleInJson)
