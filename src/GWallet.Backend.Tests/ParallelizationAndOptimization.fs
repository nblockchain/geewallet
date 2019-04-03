namespace GWallet.Backend.Tests

open System
open System.Threading
open System.Diagnostics

open NUnit.Framework

open GWallet.Backend


exception SomeExceptionDuringParallelWork

[<TestFixture>]
type ParallelizationAndOptimization() =

    let serverWithNoHistoryInfoBecauseIrrelevantToThisTest serverId job =
        { Identifier = serverId; HistoryInfo = None; Retrieval = job; }
    let dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test = (fun _ -> ())

    // yes, the default one is the fast one because it's the one with no filters, just sorting
    let default_mode_as_it_is_irrelevant_for_this_test = Mode.Fast

    [<Test>]
    member __.``calls both funcs (because it launches them in parallel)``() =
        let NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED = 2u
        // because this test doesn't deal with inconsistencies
        let NUMBER_OF_CONSISTENT_RESULTS = NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             NumberOfParallelJobsAllowed = NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED;
                             ConsistencyConfig = NumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS; }

        let mutable job1Done = false
        let aJob1 = async {
            job1Done <- true
            return 0
        }
        let mutable job2Done = false
        let aJob2 = async {
            job2Done <- true
            return 0
        }

        let func1,func2 = serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob1" aJob1,
                          serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob2" aJob2

        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        client.Query settings [ func1; func2 ]
            |> Async.RunSynchronously |> ignore

        Assert.That(job1Done, Is.True)
        Assert.That(job2Done, Is.True)

        job1Done <- false
        job2Done <- false

        //same as before, but with different order now
        client.Query settings [ func2; func1 ]
            |> Async.RunSynchronously |> ignore

        Assert.That(job1Done, Is.True)
        Assert.That(job2Done, Is.True)

    [<Test>]
    member __.``a long func doesn't block the others``() =
        let someLongTime = TimeSpan.FromSeconds 10.0

        let NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED = 2u
        // because this test doesn't deal with inconsistencies
        let NUMBER_OF_CONSISTENT_RESULTS = 1u

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             NumberOfParallelJobsAllowed = NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED;
                             ConsistencyConfig = NumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS; }

        let aJob1: Async<int> = async {
            return raise SomeExceptionDuringParallelWork
        }
        let aJob2 = async {
            do! Async.Sleep <| int someLongTime.TotalMilliseconds
            return 0
        }
        let job3Result = 1
        let aJob3 =
            async { return job3Result }

        let func1,func2,func3 = serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob1" aJob1,
                                serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob2" aJob2,
                                serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob3" aJob3

        let stopWatch = Stopwatch.StartNew()
        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        let result = client.Query settings
                                  [ func1; func2; func3 ]
                         |> Async.RunSynchronously

        stopWatch.Stop()
        Assert.That(result, Is.EqualTo job3Result)
        Assert.That(stopWatch.Elapsed, Is.LessThan(someLongTime))

    [<Test>]
    member __.``a long func doesn't block gathering more succesful results for consistency``() =
        let someLongTime = TimeSpan.FromSeconds 10.0

        let NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED = 2u
        let NUMBER_OF_CONSISTENT_RESULTS = 2u

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             NumberOfParallelJobsAllowed = NUMBER_OF_PARALLEL_JOBS_TO_BE_TESTED;
                             ConsistencyConfig = NumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS; }

        let aJob1 =
            async { return 0 }
        let aJob2 = async {
            do! Async.Sleep <| int someLongTime.TotalMilliseconds
            return 0
        }
        let aJob3 =
            async { return 0 }

        let func1,func2,func3 = serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob1" aJob1,
                                serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob2" aJob2,
                                serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob3" aJob3

        let stopWatch = Stopwatch.StartNew()
        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        let result = client.Query settings
                                  [ func1; func2; func3 ]
                         |> Async.RunSynchronously

        stopWatch.Stop()
        Assert.That(result, Is.EqualTo(0))
        Assert.That(stopWatch.Elapsed, Is.LessThan(someLongTime))

        // different order of funcs
        let stopWatch = Stopwatch.StartNew()
        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        let result = client.Query settings
                                  [ func1; func3; func2; ]
                         |> Async.RunSynchronously

        stopWatch.Stop()
        Assert.That(result, Is.EqualTo(0))
        Assert.That(stopWatch.Elapsed, Is.LessThan(someLongTime))

        // different order of funcs
        let stopWatch = Stopwatch.StartNew()
        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        let result = client.Query settings
                                  [ func3; func2; func1; ]
                         |> Async.RunSynchronously

        stopWatch.Stop()
        Assert.That(result, Is.EqualTo(0))
        Assert.That(stopWatch.Elapsed, Is.LessThan(someLongTime))

        // different order of funcs
        let stopWatch = Stopwatch.StartNew()
        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        let result = client.Query settings
                                  [ func3; func1; func2; ]
                         |> Async.RunSynchronously

        stopWatch.Stop()
        Assert.That(result, Is.EqualTo(0))
        Assert.That(stopWatch.Elapsed, Is.LessThan(someLongTime))

    [<Test>]
    member __.``using an average func encourages you (via throwing an exception) to use parallelism``() =

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                            NumberOfParallelJobsAllowed = 1u
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
                                List.Empty
                                    |> Async.RunSynchronously |> ignore
        ) |> ignore

    [<Test>]
    member __.``ordering: chooses fastest option first``() =
        let someResult1 = 1
        let someResult2 = 2
        let server1,server2 = { HistoryInfo = Some { Fault = None; TimeSpan = TimeSpan.FromSeconds 2.0 };
                                Identifier = "server1"
                                Retrieval = async { return someResult1 } },
                              { HistoryInfo = Some { Fault = None; TimeSpan = TimeSpan.FromSeconds 1.0 };
                                Identifier = "server2"
                                Retrieval = async { return someResult2 } }
        let dataRetreived = (FaultTolerantParallelClient<string, DummyIrrelevantToThisTestException>
                                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test).Query
                                (FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries())
                                [ server1; server2 ]
                                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo someResult2)

        // same but different order
        let dataRetreived = (FaultTolerantParallelClient<string, DummyIrrelevantToThisTestException>
                                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test).Query
                                (FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries())
                                [ server2; server1 ]
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
        let someResult1 = 1
        let someResult2 = 2
        let someResult3 = 3
        let server1,server2 = { HistoryInfo = Some { Fault = None; TimeSpan = TimeSpan.FromSeconds 1.0 };
                                Identifier = "server1"
                                Retrieval = async { return raise SomeExceptionDuringParallelWork } },
                              { HistoryInfo = Some { Fault = None; TimeSpan = TimeSpan.FromSeconds 2.0 };
                                Identifier = "server2"
                                Retrieval = async { return someResult2 } }
        let server3 = { HistoryInfo = None
                        Identifier = "server3"
                        Retrieval = async { return someResult3 } }
        let dataRetreived = (FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test).Query
                                { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries()
                                      with Mode = Mode.Analysis }
                                [ server1; server2; server3 ]
                                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo someResult3)

        // same but different order
        let dataRetreived = (FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test).Query
                                { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries()
                                      with Mode = Mode.Analysis }
                                [ server3; server2; server1 ]
                                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo someResult3)

