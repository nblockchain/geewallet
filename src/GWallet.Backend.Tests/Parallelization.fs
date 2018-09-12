namespace GWallet.Backend.Tests

open System
open System.Threading
open System.Diagnostics

open NUnit.Framework

open GWallet.Backend


exception SomeExceptionDuringParallelWork

[<TestFixture>]
type Parallelization() =

    [<Test>]
    member __.``calls both funcs (because it launches them in parallel)``() =
        let NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED = uint16 2
        // because this test doesn't deal with inconsistencies
        let NUMBER_OF_CONSISTENT_RESULTS = NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             NumberOfMaximumParallelJobs = NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED;
                             ConsistencyConfig = NumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS; }

        let someStringArg = "foo"
        let mutable func1Called = false
        let func1 (arg: string) =
            func1Called <- true
            0
        let mutable func2Called = false
        let func2 (arg: string) =
            func2Called <- true
            0

        let client = FaultTolerantParallelClient<SomeExceptionDuringParallelWork>()
        client.Query<string,int> settings someStringArg [ func1; func2 ]
            |> Async.RunSynchronously |> ignore

        Assert.That(func1Called, Is.True)
        Assert.That(func2Called, Is.True)

        func1Called <- false
        func2Called <- false

        //same as before, but with different order now
        client.Query<string,int> settings someStringArg [ func2; func1 ]
            |> Async.RunSynchronously |> ignore

        Assert.That(func1Called, Is.True)
        Assert.That(func2Called, Is.True)

    [<Test>]
    member __.``a long func doesn't block the others``() =
        let someLongTime = TimeSpan.FromSeconds 10.0

        let NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED = uint16 2
        // because this test doesn't deal with inconsistencies
        let NUMBER_OF_CONSISTENT_RESULTS = uint16 1

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             NumberOfMaximumParallelJobs = NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED;
                             ConsistencyConfig = NumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS; }

        let func1 (arg: string) =
            raise SomeExceptionDuringParallelWork
            0
        let func2 (arg: string) =
            Thread.Sleep someLongTime
            0
        let func3Result = 1
        let func3 (arg: string) =
            func3Result

        let someStringArg = "foo"

        let stopWatch = Stopwatch.StartNew()
        let client = FaultTolerantParallelClient<SomeExceptionDuringParallelWork>()
        let result = client.Query<string,int> settings someStringArg [ func1; func2; func3 ]
                         |> Async.RunSynchronously

        stopWatch.Stop()
        Assert.That(result, Is.EqualTo(func3Result))
        Assert.That(stopWatch.Elapsed, Is.LessThan(someLongTime))

    [<Test>]
    member __.``a long func doesn't block gathering more succesful results for consistency``() =
        let someLongTime = TimeSpan.FromSeconds 10.0

        let NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED = uint16 2
        let NUMBER_OF_CONSISTENT_RESULTS = uint16 2

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             NumberOfMaximumParallelJobs = NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED;
                             ConsistencyConfig = NumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS; }

        let func1 (arg: string) =
            0
        let func2 (arg: string) =
            Thread.Sleep someLongTime
            0
        let func3 (arg: string) =
            0

        let someStringArg = "foo"

        let stopWatch = Stopwatch.StartNew()
        let client = FaultTolerantParallelClient<SomeExceptionDuringParallelWork>()
        let result = client.Query<string,int> settings someStringArg [ func1; func2; func3 ]
                         |> Async.RunSynchronously

        stopWatch.Stop()
        Assert.That(result, Is.EqualTo(0))
        Assert.That(stopWatch.Elapsed, Is.LessThan(someLongTime))

        // different order of funcs
        let stopWatch = Stopwatch.StartNew()
        let client = FaultTolerantParallelClient<SomeExceptionDuringParallelWork>()
        let result = client.Query<string,int> settings someStringArg [ func1; func3; func2; ]
                         |> Async.RunSynchronously

        stopWatch.Stop()
        Assert.That(result, Is.EqualTo(0))
        Assert.That(stopWatch.Elapsed, Is.LessThan(someLongTime))

        // different order of funcs
        let stopWatch = Stopwatch.StartNew()
        let client = FaultTolerantParallelClient<SomeExceptionDuringParallelWork>()
        let result = client.Query<string,int> settings someStringArg [ func3; func2; func1; ]
                         |> Async.RunSynchronously

        stopWatch.Stop()
        Assert.That(result, Is.EqualTo(0))
        Assert.That(stopWatch.Elapsed, Is.LessThan(someLongTime))

        // different order of funcs
        let stopWatch = Stopwatch.StartNew()
        let client = FaultTolerantParallelClient<SomeExceptionDuringParallelWork>()
        let result = client.Query<string,int> settings someStringArg [ func3; func1; func2; ]
                         |> Async.RunSynchronously

        stopWatch.Stop()
        Assert.That(result, Is.EqualTo(0))
        Assert.That(stopWatch.Elapsed, Is.LessThan(someLongTime))

    [<Test>]
    member __.``using an average func encourages you (via throwing an exception) to use parallelism``() =

        let someStringArg = "foo"
        let someReturnValue = 0
        let func1 (arg: string) =
            someReturnValue
        let func2 (arg: string) =
            someReturnValue
        let func3 (arg: string) =
            someReturnValue

        let funcs = [ func1; func2; func3; ]

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                            NumberOfMaximumParallelJobs = uint16 1
                            ConsistencyConfig =
                                AverageBetweenResponses (uint16 2,
                                                         (fun _ ->
                                                             failwith "unreachable"
                                                         )); }

        let client = FaultTolerantParallelClient<SomeExceptionDuringParallelWork>()

        // because 2>1
        Assert.Throws<ArgumentException>(
            fun _ -> client.Query<string,int>
                            settings
                            "_" List.Empty |> Async.RunSynchronously |> ignore
        ) |> ignore

    [<Test>]
    // this bug is probably the reason why the XamForms UI gets frozen after some time... too many unkilled threads
    [<Ignore("not fixed yet")>]
    member __.``slower funcs get cancelled after consistent results have been gathered``() =
        let someLongTime = TimeSpan.FromSeconds 1.0

        let mutable longFuncFinishedExecution = false
        let func1 (arg: unit) =
            0
        let func2 (arg: unit) =
            0
        let longFuncThatShouldBeCancelled (arg: unit) =
            Thread.Sleep someLongTime
            longFuncFinishedExecution <- true
            0

        let allFuncs = [ func1; func2; longFuncThatShouldBeCancelled ]
        let number_of_parallel_jobs_allowed = uint16 allFuncs.Length
        let NUMBER_OF_CONSISTENT_RESULTS = uint16 2

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             NumberOfMaximumParallelJobs = number_of_parallel_jobs_allowed;
                             ConsistencyConfig = NumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS; }

        let client = FaultTolerantParallelClient<SomeExceptionDuringParallelWork>()
        let result = client.Query<unit,int> settings () allFuncs
                         |> Async.RunSynchronously

        Assert.That(result, Is.EqualTo 0)

        // we sleep longer than someLongTime, to make sure longFunc is finished
        Thread.Sleep(someLongTime + someLongTime)

        Assert.That(longFuncFinishedExecution, Is.EqualTo false)

