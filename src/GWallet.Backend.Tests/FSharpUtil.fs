namespace GWallet.Backend.Tests

open System
open System.Threading.Tasks

open NUnit.Framework

open GWallet.Backend

[<TestFixture>]
type FSharpUtilCoverage() =

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
