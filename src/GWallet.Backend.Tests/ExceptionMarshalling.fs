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

    let SerializeBasicException () =
        let ex = Exception "msg"
        Marshalling.Serialize ex

    let SerializeRealException () =
        let someEx = Exception "msg"
        let ex =
            try
                raise someEx
                someEx
            with
            | ex ->
                ex
        Marshalling.Serialize ex

    let SerializeInnerException () =
        let ex = Exception("msg", Exception "innerMsg")
        Marshalling.Serialize ex

    [<Test>]
    member __.``can serialize basic exceptions``() =
        let json = SerializeBasicException ()
        Assert.That(json, Is.Not.Null)
        Assert.That(json, Is.Not.Empty)
        Assert.That(MarshallingData.SerializedExceptionsAreSame json MarshallingData.BasicExceptionExampleInJson)

    [<Test>]
    member __.``can deserialize basic exceptions``() =
        let basicExSerialized =
            try
                SerializeBasicException ()
            with
            | _ ->
                Assert.Inconclusive "Fix the serialization test first"
                failwith "unreachable"

        let ex: Exception = Marshalling.Deserialize basicExSerialized
        Assert.That(ex, Is.Not.Null)
        Assert.That(ex, Is.InstanceOf<Exception>())
        Assert.That(ex.Message, Is.EqualTo "msg")
        Assert.That(ex.InnerException, Is.Null)
        Assert.That(ex.StackTrace, Is.Null)

    [<Test>]
    member __.``can serialize real exceptions``() =
        let json = SerializeRealException ()
        Assert.That(json, Is.Not.Null)
        Assert.That(json, Is.Not.Empty)
        Assert.That(MarshallingData.SerializedExceptionsAreSame json MarshallingData.RealExceptionExampleInJson)

    [<Test>]
    member __.``can deserialize real exceptions``() =
        let realExceptionSerialized =
            try
                SerializeRealException ()
            with
            | _ ->
                Assert.Inconclusive "Fix the serialization test first"
                failwith "unreachable"

        let ex: Exception = Marshalling.Deserialize realExceptionSerialized
        Assert.That(ex, Is.Not.Null)
        Assert.That(ex, Is.InstanceOf<Exception>())
        Assert.That(ex.Message, Is.EqualTo "msg")
        Assert.That(ex.InnerException, Is.Null)
        Assert.That(ex.StackTrace, Is.Not.Null)
        Assert.That(ex.StackTrace, Is.Not.Empty)

    [<Test>]
    member __.``can serialize inner exceptions``() =
        let json = SerializeInnerException ()
        Assert.That(json, Is.Not.Null)
        Assert.That(json, Is.Not.Empty)
        Assert.That(MarshallingData.SerializedExceptionsAreSame json MarshallingData.InnerExceptionExampleInJson)

    [<Test>]
    member __.``can deserialize inner exceptions``() =
        let innerExceptionSerialized =
            try
                SerializeInnerException ()
            with
            | _ ->
                Assert.Inconclusive "Fix the serialization test first"
                failwith "unreachable"

        let ex: Exception = Marshalling.Deserialize innerExceptionSerialized
        Assert.That (ex, Is.Not.Null)
        Assert.That (ex, Is.InstanceOf<Exception>())
        Assert.That (ex.Message, Is.EqualTo "msg")
        Assert.That (ex.StackTrace, Is.Null)
        Assert.That (ex.InnerException, Is.Not.Null)

        Assert.That (ex.InnerException, Is.InstanceOf<Exception>())
        Assert.That (ex.InnerException.Message, Is.EqualTo "innerMsg")
        Assert.That (ex.InnerException.StackTrace, Is.Null)

    [<Test>]
    [<Ignore "NIE">]
    member __.``can serialize custom exceptions``() =
        let ex = CustomException "msg"
        let json = Marshalling.Serialize ex
        Assert.That(json, Is.Not.Null)
        Assert.That(json, Is.Not.Empty)
        Assert.That(json |> MarshallingData.Sanitize,
                    Is.EqualTo MarshallingData.CustomExceptionExampleInJson)

    [<Test>]
    [<Ignore "NIE">]
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

        Assert.That(MarshallingData.SerializedExceptionsAreSame json MarshallingData.FullExceptionExampleInJson)

