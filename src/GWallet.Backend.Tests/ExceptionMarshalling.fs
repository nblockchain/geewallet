namespace GWallet.Backend.Tests

open System

open NUnit.Framework

open GWallet.Backend


type CustomExceptionWithoutInnerExceptionCtor =
   inherit Exception

   new(message) =
       { inherit Exception(message) }

type CustomException =
   inherit Exception

   new(message: string, innerException: CustomException) =
       { inherit Exception(message, innerException) }
   new(message) =
       { inherit Exception(message) }

exception CustomFSharpException


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

    let SerializeCustomException () =
        let ex = CustomException "msg"
        Marshalling.Serialize ex

    let SerializeCustomFSharpException () =
        let ex = CustomFSharpException
        Marshalling.Serialize ex

    let SerializeFullException () =
        let someCEx = CustomException("msg", CustomException "innerMsg")
        let ex =
            try
                raise someCEx
                someCEx
            with
            | :? CustomException as cex ->
                cex
        Marshalling.Serialize ex

    let msg = "Expected Exception.ToString() differs from actual"
#if LEGACY_FRAMEWORK
    let legacyIgnoreMsg = "Mono or old .NETFramework might vary slightly; there's no need to really do any regression testing here"
#endif

    [<Test>]
    member __.``can serialize basic exceptions``() =
        let json = SerializeBasicException ()
        Assert.That(json, Is.Not.Null)
        Assert.That(json.Trim(), Is.Not.Empty)
        Assert.That(MarshallingData.SerializedExceptionsAreSame json MarshallingData.BasicExceptionExampleInJson msg)

    [<Test>]
    member __.``can deserialize basic exceptions``() =
        let basicExSerialized =
            try
                SerializeBasicException ()
            with
            | _ ->
                Assert.Inconclusive "Fix the serialization test first"
                failwith "unreachable"

        let ex: MarshalledException = Marshalling.Deserialize basicExSerialized
        Assert.That(ex, Is.Not.Null)
        Assert.That(ex, Is.InstanceOf<MarshalledException>())
        Assert.That(ex.FullDescription.Trim().Length, Is.GreaterThan 0)
        Assert.That(
            MarshallingData.Sanitize ex.FullDescription,
            Is.EqualTo (MarshallingData.Sanitize "System.Exception: msg")
        )

    [<Test>]
    member __.``can serialize real exceptions``() =
        let json = SerializeRealException ()
        Assert.That(json, Is.Not.Null)
        Assert.That(json.Trim(), Is.Not.Empty)
#if !LEGACY_FRAMEWORK
        Assert.That(MarshallingData.SerializedExceptionsAreSame json MarshallingData.RealExceptionExampleInJson msg)
#else
        Assert.Ignore legacyIgnoreMsg
#endif

    [<Test>]
    member __.``can deserialize real exceptions``() =
        let realExceptionSerialized =
            try
                SerializeRealException ()
            with
            | _ ->
                Assert.Inconclusive "Fix the serialization test first"
                failwith "unreachable"

        let ex: MarshalledException = Marshalling.Deserialize realExceptionSerialized
        Assert.That(ex, Is.Not.Null)
        Assert.That(ex, Is.InstanceOf<MarshalledException>())
        Assert.That(ex.FullDescription.Trim().Length, Is.GreaterThan 0)
#if !LEGACY_FRAMEWORK
        let expected =
            sprintf
                "System.Exception: msg   at GWallet.Backend.Tests.ExceptionMarshalling.SerializeRealException() in %s/ExceptionMarshalling.fs:line 38"
                MarshallingData.ThisProjPath
        Assert.That(
            MarshallingData.Sanitize ex.FullDescription,
            Is.EqualTo (MarshallingData.Sanitize expected)
        )
#else
        Assert.Ignore legacyIgnoreMsg
#endif

    [<Test>]
    member __.``can serialize inner exceptions``() =
        let json = SerializeInnerException ()
        Assert.That(json, Is.Not.Null)
        Assert.That(json.Trim(), Is.Not.Empty)
        Assert.That(MarshallingData.SerializedExceptionsAreSame json MarshallingData.InnerExceptionExampleInJson msg)

    [<Test>]
    member __.``can deserialize inner exceptions``() =
        let innerExceptionSerialized =
            try
                SerializeInnerException ()
            with
            | _ ->
                Assert.Inconclusive "Fix the serialization test first"
                failwith "unreachable"

        let ex: MarshalledException = Marshalling.Deserialize innerExceptionSerialized
        Assert.That (ex, Is.Not.Null)
        Assert.That (ex, Is.InstanceOf<MarshalledException>())
        Assert.That(ex.FullDescription.Trim().Length, Is.GreaterThan 0)
        Assert.That (
            MarshallingData.Sanitize ex.FullDescription,
            Is.EqualTo (MarshallingData.Sanitize "System.Exception: msg ---> System.Exception: innerMsg   --- End of inner exception stack trace ---")
        )

    [<Test>]
    member __.``can serialize custom exceptions``() =
        let json = SerializeCustomException ()
        Assert.That(json, Is.Not.Null)
        Assert.That(json.Trim(), Is.Not.Empty)
        Assert.That(MarshallingData.SerializedExceptionsAreSame json MarshallingData.CustomExceptionExampleInJson msg)

    [<Test>]
    member __.``serializing custom exception without inner ex ctor does not crash``() =
        let exToSerialize = CustomExceptionWithoutInnerExceptionCtor "msg"
        let serializedEx = (Marshalling.Serialize exToSerialize).Trim()
        Assert.That(serializedEx, Is.Not.Null)
        Assert.That(serializedEx.Trim().Length, Is.GreaterThan 0)

    [<Test>]
    member __.``can deserialize custom exceptions``() =
        let customExceptionSerialized =
            try
                SerializeCustomException ()
            with
            | _ ->
                Assert.Inconclusive "Fix the serialization test first"
                failwith "unreachable"

        let ex: MarshalledException = Marshalling.Deserialize customExceptionSerialized
        Assert.That(ex, Is.Not.Null)
        Assert.That(ex, Is.InstanceOf<MarshalledException>())
        Assert.That(
            MarshallingData.Sanitize ex.FullDescription,
            Is.EqualTo (MarshallingData.Sanitize "GWallet.Backend.Tests.CustomException: msg")
        )

    [<Test>]
    member __.``can serialize F# custom exceptions``() =
        let json = SerializeCustomFSharpException ()
        Assert.That(json, Is.Not.Null)
        Assert.That(json.Trim(), Is.Not.Empty)
#if !LEGACY_FRAMEWORK
        Assert.That(MarshallingData.SerializedExceptionsAreSame json MarshallingData.CustomFSharpExceptionExampleInJson msg)
#else
        Assert.Ignore legacyIgnoreMsg
#endif

    [<Test>]
    member __.``can deserialize F# custom exceptions``() =
        let customExceptionSerialized =
            try
                SerializeCustomFSharpException ()
            with
            | _ ->
                Assert.Inconclusive "Fix the serialization test first"
                failwith "unreachable"

        let ex: MarshalledException = Marshalling.Deserialize customExceptionSerialized
        Assert.That(ex, Is.Not.Null)
        Assert.That(ex, Is.InstanceOf<MarshalledException>())
        Assert.That(ex.FullDescription.Trim().Length, Is.GreaterThan 0)

        if ex.FullDescription.Contains "of type" then
            // old version of .NET6? (happens in stockdotnet6 CI lanes)
            Assert.That(
                MarshallingData.Sanitize ex.FullDescription,
                Is.EqualTo (MarshallingData.Sanitize "GWallet.Backend.Tests.CustomFSharpException: Exception of type 'GWallet.Backend.Tests.CustomFSharpException' was thrown.")
            )
        else
            Assert.That(
                MarshallingData.Sanitize ex.FullDescription,
                Is.EqualTo (MarshallingData.Sanitize "GWallet.Backend.Tests.CustomFSharpException: CustomFSharpException")
            )


    [<Test>]
    member __.``can serialize full exceptions (all previous features combined)``() =
        let json = SerializeFullException ()

        Assert.That(json, Is.Not.Null)
        Assert.That(json.Trim(), Is.Not.Empty)
#if !LEGACY_FRAMEWORK
        Assert.That(MarshallingData.SerializedExceptionsAreSame json MarshallingData.FullExceptionExampleInJson msg)
#else
        Assert.Ignore legacyIgnoreMsg
#endif

    [<Test>]
    member __.``can deserialize full exceptions (all previous features combined)``() =
        let fullExceptionSerialized =
            try
                SerializeFullException ()
            with
            | _ ->
                Assert.Inconclusive "Fix the serialization test first"
                failwith "unreachable"

        let ex: MarshalledException = Marshalling.Deserialize fullExceptionSerialized
        Assert.That(ex, Is.Not.Null)
        Assert.That(ex, Is.InstanceOf<MarshalledException> ())
        Assert.That(ex.FullDescription.Trim().Length, Is.GreaterThan 0)

#if !LEGACY_FRAMEWORK
        Assert.That(
            MarshallingData.Sanitize ex.FullDescription,
            Is.EqualTo (
                MarshallingData.Sanitize
                <| sprintf
                    "GWallet.Backend.Tests.CustomException: msg ---> GWallet.Backend.Tests.CustomException: innerMsg   --- End of inner exception stack trace ---   at GWallet.Backend.Tests.ExceptionMarshalling.SerializeFullException() in %s/ExceptionMarshalling.fs:line 61"
                    MarshallingData.ThisProjPath
            )
        )
#else
        Assert.Ignore legacyIgnoreMsg
#endif


