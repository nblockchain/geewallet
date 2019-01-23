namespace GWallet.Backend.Tests

open System
open System.Threading
open System.Diagnostics

open NUnit.Framework

open GWallet.Backend


exception SomeExceptionDuringParallelWork

[<TestFixture>]
type ParallelizationAndOptimization() =

    let serverWithNoHistoryInfoBecauseIrrelevantToThisTest serverId func =
        { Identifier = serverId; HistoryInfo = None; Retreival = func; }
    let dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test = (fun _ -> ())

    // yes, the default one is the fast one because it's the one with no filters, just sorting
    let default_mode_as_it_is_irrelevant_for_this_test = Mode.Fast

    [<Test>]
    member __.``calls both funcs (because it launches them in parallel)``() =
        let NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED = 2u
        // because this test doesn't deal with inconsistencies
        let NUMBER_OF_CONSISTENT_RESULTS = NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             NumberOfMaximumParallelJobs = NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED;
                             ConsistencyConfig = NumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS; }

        let someStringArg = "foo"
        let mutable func1Called = false
        let aFunc1 (arg: string) =
            func1Called <- true
            0
        let mutable func2Called = false
        let aFunc2 (arg: string) =
            func2Called <- true
            0

        let func1,func2 = serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aFunc1" aFunc1,
                          serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aFunc2" aFunc2

        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        client.Query settings someStringArg [ func1; func2 ] default_mode_as_it_is_irrelevant_for_this_test
            |> Async.RunSynchronously |> ignore

        Assert.That(func1Called, Is.True)
        Assert.That(func2Called, Is.True)

        func1Called <- false
        func2Called <- false

        //same as before, but with different order now
        client.Query settings someStringArg [ func2; func1 ] default_mode_as_it_is_irrelevant_for_this_test
            |> Async.RunSynchronously |> ignore

        Assert.That(func1Called, Is.True)
        Assert.That(func2Called, Is.True)

    [<Test>]
    member __.``a long func doesn't block the others``() =
        let someLongTime = TimeSpan.FromSeconds 10.0

        let NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED = 2u
        // because this test doesn't deal with inconsistencies
        let NUMBER_OF_CONSISTENT_RESULTS = 1u

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             NumberOfMaximumParallelJobs = NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED;
                             ConsistencyConfig = NumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS; }

        let aFunc1 (arg: string) =
            raise SomeExceptionDuringParallelWork
            0
        let aFunc2 (arg: string) =
            Thread.Sleep someLongTime
            0
        let func3Result = 1
        let aFunc3 (arg: string) =
            func3Result

        let func1,func2,func3 = serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aFunc1" aFunc1,
                                serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aFunc2" aFunc2,
                                serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aFunc3" aFunc3

        let someStringArg = "foo"

        let stopWatch = Stopwatch.StartNew()
        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        let result = client.Query settings
                                  someStringArg
                                  [ func1; func2; func3 ]
                                  default_mode_as_it_is_irrelevant_for_this_test
                         |> Async.RunSynchronously

        stopWatch.Stop()
        Assert.That(result, Is.EqualTo(func3Result))
        Assert.That(stopWatch.Elapsed, Is.LessThan(someLongTime))

    [<Test>]
    member __.``a long func doesn't block gathering more succesful results for consistency``() =
        let someLongTime = TimeSpan.FromSeconds 10.0

        let NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED = 2u
        let NUMBER_OF_CONSISTENT_RESULTS = 2u

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             NumberOfMaximumParallelJobs = NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED;
                             ConsistencyConfig = NumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS; }

        let aFunc1 (arg: string) =
            0
        let aFunc2 (arg: string) =
            Thread.Sleep someLongTime
            0
        let aFunc3 (arg: string) =
            0

        let func1,func2,func3 = serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aFunc1" aFunc1,
                                serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aFunc2" aFunc2,
                                serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aFunc3" aFunc3

        let someStringArg = "foo"

        let stopWatch = Stopwatch.StartNew()
        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        let result = client.Query settings
                                  someStringArg
                                  [ func1; func2; func3 ]
                                  default_mode_as_it_is_irrelevant_for_this_test
                         |> Async.RunSynchronously

        stopWatch.Stop()
        Assert.That(result, Is.EqualTo(0))
        Assert.That(stopWatch.Elapsed, Is.LessThan(someLongTime))

        // different order of funcs
        let stopWatch = Stopwatch.StartNew()
        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        let result = client.Query settings
                                  someStringArg
                                  [ func1; func3; func2; ]
                                  default_mode_as_it_is_irrelevant_for_this_test
                         |> Async.RunSynchronously

        stopWatch.Stop()
        Assert.That(result, Is.EqualTo(0))
        Assert.That(stopWatch.Elapsed, Is.LessThan(someLongTime))

        // different order of funcs
        let stopWatch = Stopwatch.StartNew()
        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        let result = client.Query settings
                                  someStringArg
                                  [ func3; func2; func1; ]
                                  default_mode_as_it_is_irrelevant_for_this_test
                         |> Async.RunSynchronously

        stopWatch.Stop()
        Assert.That(result, Is.EqualTo(0))
        Assert.That(stopWatch.Elapsed, Is.LessThan(someLongTime))

        // different order of funcs
        let stopWatch = Stopwatch.StartNew()
        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        let result = client.Query settings
                                  someStringArg
                                  [ func3; func1; func2; ]
                                  default_mode_as_it_is_irrelevant_for_this_test
                         |> Async.RunSynchronously

        stopWatch.Stop()
        Assert.That(result, Is.EqualTo(0))
        Assert.That(stopWatch.Elapsed, Is.LessThan(someLongTime))

    [<Test>]
    member __.``using an average func encourages you (via throwing an exception) to use parallelism``() =

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                            NumberOfMaximumParallelJobs = 1u
                            ConsistencyConfig =
                                AverageBetweenResponses (2u,
                                                         (fun _ ->
                                                             failwith "unreachable"
                                                         )); }

        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test

        // because 2>1
        Assert.Throws<ArgumentException>(
            fun _ -> client.Query
                                settings
                                "_"
                                List.Empty
                                default_mode_as_it_is_irrelevant_for_this_test
                                    |> Async.RunSynchronously |> ignore
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

        let allFuncs = [ serverWithNoHistoryInfoBecauseIrrelevantToThisTest "func1" func1
                         serverWithNoHistoryInfoBecauseIrrelevantToThisTest "func1" func2
                         serverWithNoHistoryInfoBecauseIrrelevantToThisTest "longFunc" longFuncThatShouldBeCancelled ]
        let number_of_parallel_jobs_allowed = uint32 allFuncs.Length
        let NUMBER_OF_CONSISTENT_RESULTS = 2u

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             NumberOfMaximumParallelJobs = number_of_parallel_jobs_allowed;
                             ConsistencyConfig = NumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS; }

        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        let result = client.Query settings () allFuncs default_mode_as_it_is_irrelevant_for_this_test
                         |> Async.RunSynchronously

        Assert.That(result, Is.EqualTo 0)

        // we sleep longer than someLongTime, to make sure longFunc is finished
        Thread.Sleep(someLongTime + someLongTime)

        Assert.That(longFuncFinishedExecution, Is.EqualTo false)

    [<Test>]
    member __.``ordering: chooses fastest option first``() =
        let someStringArg = "foo"
        let someResult1 = 1
        let someResult2 = 2
        let server1,server2 = { HistoryInfo = Some { Fault = None; TimeSpan = TimeSpan.FromSeconds 2.0 };
                                Identifier = "server1"; Retreival = (fun arg -> someResult1) },
                              { HistoryInfo = Some { Fault = None; TimeSpan = TimeSpan.FromSeconds 1.0 };
                                Identifier = "server2"; Retreival = (fun arg -> someResult2) }
        let dataRetreived = (FaultTolerantParallelClient<string, DummyIrrelevantToThisTestException>
                                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test).Query
                                (FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries())
                                someStringArg [ server1; server2 ]
                                default_mode_as_it_is_irrelevant_for_this_test
                                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo someResult2)

        // same but different order
        let dataRetreived = (FaultTolerantParallelClient<string, DummyIrrelevantToThisTestException>
                                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test).Query
                                (FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries())
                                someStringArg [ server2; server1 ]
                                default_mode_as_it_is_irrelevant_for_this_test
                                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo someResult2)

    [<Test>]
    member __.``ListIntersect metatest`` () =
        let res = FSharpUtil.ListIntersect [ 10; 20; 30; ] [ 1; 2; ] 2u
        Assert.That(res.Length, Is.EqualTo 5)
        Assert.That(res.[0], Is.EqualTo 10)
        Assert.That(res.[1], Is.EqualTo 1)
        Assert.That(res.[2], Is.EqualTo 20)
        Assert.That(res.[3], Is.EqualTo 2)
        Assert.That(res.[4], Is.EqualTo 30)

        let res = FSharpUtil.ListIntersect [ 10; 20; 30; 40; 50; ] [ 1; 2; ] 3u
        Assert.That(res.Length, Is.EqualTo 7)
        Assert.That(res.[0], Is.EqualTo 10)
        Assert.That(res.[1], Is.EqualTo 20)
        Assert.That(res.[2], Is.EqualTo 1)
        Assert.That(res.[3], Is.EqualTo 30)
        Assert.That(res.[4], Is.EqualTo 40)
        Assert.That(res.[5], Is.EqualTo 2)
        Assert.That(res.[6], Is.EqualTo 50)

    [<Test>]
    member __.``ordering: servers lacking history come always first in analysis(non-fast) mode``() =
        let someStringArg = "foo"
        let someResult1 = 1
        let someResult2 = 2
        let someResult3 = 3
        let server1,server2 = { HistoryInfo = Some { Fault = None; TimeSpan = TimeSpan.FromSeconds 1.0 };
                                Identifier = "server1"; Retreival = (fun arg -> raise SomeExceptionDuringParallelWork) },
                              { HistoryInfo = Some { Fault = None; TimeSpan = TimeSpan.FromSeconds 2.0 };
                                Identifier = "server2"; Retreival = (fun arg -> someResult2) }
        let server3 = { HistoryInfo = None
                        Identifier = "server3"; Retreival = (fun arg -> someResult3) }
        let dataRetreived = (FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test).Query
                                (FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries())
                                someStringArg [ server1; server2; server3 ]
                                Mode.Analysis
                                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo someResult3)

        // same but different order
        let dataRetreived = (FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test).Query
                                (FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries())
                                someStringArg [ server3; server2; server1 ]
                                Mode.Analysis
                                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo someResult3)

