namespace GWallet.Backend.Tests

open System
open System.Threading.Tasks

open NUnit.Framework

open GWallet.Backend

type UnexpectedTaskCanceledException(message: string, innerException) =
    inherit TaskCanceledException (message, innerException)

[<TestFixture>]
type FSharpUtilCoverage() =

    [<Test>]
    member __.``find exception: basic test``() =
        let innerEx = TaskCanceledException "bar"
        let wrapperEx = Exception("foo", innerEx)
        let childFound = FSharpUtil.FindException<TaskCanceledException> wrapperEx
        match childFound with
        | None -> failwith "should find through inner classes"
        | Some ex ->
            Assert.That(Object.ReferenceEquals(ex, innerEx), Is.True)
            Assert.That(Object.ReferenceEquals(ex.InnerException, null))

    [<Test>]
    member __.``find exception: it works with inherited classes (UnexpectedTaskCanceledException is child of TaskCanceledException)``() =
        let innerEx = TaskCanceledException "bar"
        let inheritedEx = UnexpectedTaskCanceledException("foo", innerEx)
        let parentFound = FSharpUtil.FindException<TaskCanceledException> inheritedEx
        match parentFound with
        | None -> failwith "should work with derived classes"
        | Some ex ->
            Assert.That(Object.ReferenceEquals(ex, inheritedEx), Is.True)
            Assert.That(Object.ReferenceEquals(ex.InnerException, innerEx))

    [<Test>]
    member __.``find exception: flattens (AggregateEx)``() =
        let innerEx1 = TaskCanceledException "bar" :> Exception
        let innerEx2 = UnexpectedTaskCanceledException ("baz", null) :> Exception
        let parent = AggregateException("foo", [|innerEx1; innerEx2|])
        let sibling1Found = FSharpUtil.FindException<TaskCanceledException> parent
        match sibling1Found with
        | None -> failwith "should work"
        | Some ex ->
            Assert.That(Object.ReferenceEquals(ex, innerEx1), Is.True)
        let sibling2Found = FSharpUtil.FindException<UnexpectedTaskCanceledException> parent
        match sibling2Found with
        | None -> failwith "should find sibling 2 too"
        | Some ex ->
            Assert.That(Object.ReferenceEquals(ex, innerEx2), Is.True)
