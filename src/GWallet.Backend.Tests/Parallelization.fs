namespace GWallet.Backend.Tests

open NUnit.Framework

open GWallet.Backend

module Parallelization =

    exception SomeException
    [<Test>]
    let ``calls both funcs (because it launches them in parallel)``() =
        let NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED = 2
        // because this test doesn't deal with inconsistencies
        let NUMBER_OF_CONSISTENT_RESULTS = NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED

        let someStringArg = "foo"
        let mutable func1Called = false
        let func1 (arg: string) =
            func1Called <- true
            0
        let mutable func2Called = false
        let func2 (arg: string) =
            func2Called <- true
            0

        let client = FaultTolerantParallelClient<SomeException>(NUMBER_OF_CONSISTENT_RESULTS,
                                                                NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED)
        client.Query<string,int> someStringArg [ func1; func2 ]
            |> Async.RunSynchronously |> ignore

        Assert.That(func1Called, Is.True)
        Assert.That(func2Called, Is.True)

        func1Called <- false
        func2Called <- false

        //same as before, but with different order now
        client.Query<string,int> someStringArg [ func2; func1 ]
            |> Async.RunSynchronously |> ignore

        Assert.That(func1Called, Is.True)
        Assert.That(func2Called, Is.True)
