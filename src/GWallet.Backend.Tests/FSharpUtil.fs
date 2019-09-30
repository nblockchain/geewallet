namespace GWallet.Backend.Tests

open System
open System.Threading.Tasks

open NUnit.Framework

open GWallet.Backend

[<TestFixture>]
type FSharpUtilCoverage() =

    [<Test>]
    member __.``basic test``() =
        let innerEx = TaskCanceledException "bar"
        let wrapperEx = Exception("foo", innerEx)
        let childFound = FSharpUtil.FindException<TaskCanceledException> wrapperEx
        match childFound with
        | None -> failwith "should find through inner classes"
        | Some ex ->
            Assert.That(Object.ReferenceEquals(ex, innerEx), Is.True)
            Assert.That(Object.ReferenceEquals(ex.InnerException, null))

    [<Test>]
    member __.``it works with inherited classes (UnexpectedTaskCanceledException is child of TaskCanceledException)``() =
        let innerEx = TaskCanceledException "bar"
        let inheritedEx = UnexpectedTaskCanceledException("foo", innerEx)
        let parentFound = FSharpUtil.FindException<TaskCanceledException> inheritedEx
        match parentFound with
        | None -> failwith "should work with derived classes"
        | Some ex ->
            Assert.That(Object.ReferenceEquals(ex, inheritedEx), Is.True)
            Assert.That(Object.ReferenceEquals(ex.InnerException, innerEx))
