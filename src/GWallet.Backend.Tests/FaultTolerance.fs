namespace GWallet.Backend.Tests

open System
open System.Linq

open NUnit.Framework

open GWallet.Backend

exception DummyIrrelevantToThisTestException
exception SomeSpecificException
exception SomeException
exception SomeOtherException
type SomeInnerException() =
   inherit Exception()

[<TestFixture>]
type FaultTolerance() =

    let one_consistent_result_because_this_test_doesnt_test_consistency = 1u
    let not_more_than_one_parallel_job_because_this_test_doesnt_test_parallelization = 1u
    let test_does_not_involve_retries = 0u
    let dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test = (fun _ -> ())

    // yes, the default one is the fast one because it's the one with no filters, just sorting
    let default_mode_as_it_is_irrelevant_for_this_test = Mode.Fast

    let defaultSettingsForNoConsistencyNoParallelismAndNoRetries() =
        {
            NumberOfMinimumParallelJobs = not_more_than_one_parallel_job_because_this_test_doesnt_test_parallelization
            NumberOfMaximumParallelJobs = not_more_than_one_parallel_job_because_this_test_doesnt_test_parallelization
            TimeoutToSpawnMoreParallelJobs = None
            ConsistencyConfig = NumberOfConsistentResponsesRequired one_consistent_result_because_this_test_doesnt_test_consistency
            NumberOfRetries = test_does_not_involve_retries
            NumberOfRetriesForInconsistency = test_does_not_involve_retries
            Mode = default_mode_as_it_is_irrelevant_for_this_test

            // this setting below is not being tested
            ShouldReportUncancelledJobs = false
        }

    let defaultFaultTolerantParallelClient =
        FaultTolerantParallelClient<string,SomeSpecificException>
            dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test

    let serverWithNoHistoryInfoBecauseIrrelevantToThisTest serverId job =
        { Identifier = serverId; HistoryInfo = None; Retrieval = job; }

    [<Test>]
    member __.``can retrieve basic T for single func``() =
        let someResult = 1
        let aJob =
            async { return someResult }
        let func = serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob" aJob
        let dataRetreived =
            defaultFaultTolerantParallelClient.Query
                (defaultSettingsForNoConsistencyNoParallelismAndNoRetries()) [ func ]
                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo(someResult))

    [<Test>]
    member __.``can retrieve basic T for 2 funcs``() =
        let someResult = 1
        let aJob1 =
            async { return someResult }
        let aJob2 =
            async { return someResult }
        let func1,func2 = serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob1" aJob1,
                          serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob2" aJob2
        let dataRetreived = defaultFaultTolerantParallelClient.Query
                                (defaultSettingsForNoConsistencyNoParallelismAndNoRetries())
                                [ func1; func2 ]
                                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo(someResult))

    [<Test>]
    member __.``throws ArgumentException if no funcs``(): unit =
        let client = defaultFaultTolerantParallelClient
        Assert.Throws<ArgumentException>(
            fun _ -> client.Query
                            (defaultSettingsForNoConsistencyNoParallelismAndNoRetries())
                            List.Empty
                                |> Async.RunSynchronously |> ignore
        ) |> ignore

    [<Test>]
    member __.``can retrieve one if 1 of the funcs throws``() =
        let someResult = 1
        let aJob1 =
            async { return raise SomeSpecificException }
        let aJob2 =
            async { return someResult }
        let func1,func2 = serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob1" aJob1,
                          serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob2" aJob2
        let dataRetreived =
            defaultFaultTolerantParallelClient.Query
                (defaultSettingsForNoConsistencyNoParallelismAndNoRetries()) [ func1; func2 ]
                    |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo(someResult))

    [<Test>]
    member __.``ServerUnavailabilityException exception contains innerException``() =
        let aJob1 =
            async { return raise SomeException }
        let aJob2 =
            async { return raise SomeException }

        let func1,func2 = serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob1" aJob1,
                          serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob2" aJob2

        let dataRetrieved =
            try
                let result =
                    (FaultTolerantParallelClient<string,SomeException>
                        dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test)
                        .Query
                            (defaultSettingsForNoConsistencyNoParallelismAndNoRetries()) [ func1; func2 ]
                                |> Async.RunSynchronously
                Some(result)
            with
            | ex ->
                Assert.That (ex :? ResourceUnavailabilityException, Is.True)
                Assert.That (ex.GetType().Name, Is.EqualTo "NoneAvailableException")
                Assert.That (ex.InnerException, Is.Not.Null)
                Assert.That (ex.InnerException, Is.TypeOf<SomeException>())
                None

        Assert.That(dataRetrieved, Is.EqualTo(None))

    [<Test>]
    member __.``exception type passed in is ignored``() =
        let someResult = 1
        let aJob1 =
            async { return raise SomeException }
        let aJob2 =
            async { return someResult }
        let func1,func2 = serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob1" aJob1,
                          serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob2" aJob2

        let result =
            (FaultTolerantParallelClient<string, SomeException>
                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test)
                .Query
                    (defaultSettingsForNoConsistencyNoParallelismAndNoRetries()) [ func1; func2 ]
                        |> Async.RunSynchronously
        Assert.That(result, Is.EqualTo(someResult))

    [<Test>]
    member __.``exception type not passed in is not ignored``() =
        let someResult = 1
        let aJob1 =
            async { return raise SomeOtherException }
        let aJob2 =
            async { return someResult }
        let func1,func2 = serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob1" aJob1,
                          serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob2" aJob2

        let ex = Assert.Throws<AggregateException>(fun _ ->
            (FaultTolerantParallelClient<string, SomeException>
                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test)
                .Query
                    (defaultSettingsForNoConsistencyNoParallelismAndNoRetries()) [ func1; func2 ]
                        |> Async.RunSynchronously
                            |> ignore )

        Assert.That((FSharpUtil.FindException<SomeOtherException> ex).IsSome, Is.True)

    [<Test>]
    member __.``exception type passed in is ignored also if it's found in an innerException'``() =
        let someResult = 1
        let aJob1 =
            async { return raise <| Exception("bar", SomeInnerException()) }
        let aJob2 =
            async { return someResult }

        let func1,func2 = serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob1" aJob1,
                          serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob2" aJob2

        let result =
            (FaultTolerantParallelClient<string, SomeInnerException>
                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test)
                .Query
                    (defaultSettingsForNoConsistencyNoParallelismAndNoRetries()) [ func1; func2 ]
                        |> Async.RunSynchronously
        Assert.That(result, Is.EqualTo(someResult))

    [<Test>]
    member __.``exception passed in must not be SystemException, otherwise it throws``() =
        Assert.Throws<ArgumentException>(fun _ ->
            (FaultTolerantParallelClient<string, Exception>
                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test)
                |> ignore ) |> ignore

    [<Test>]
    member __.``makes sure data is consistent across N funcs``() =
        let numberOfConsistentResponsesToBeConsideredSafe = 2u

        let someConsistentResult = 1
        let someInconsistentResult = 2

        let anInconsistentJob =
            async { return someInconsistentResult }
        let aConsistentJobA =
            async { return someConsistentResult }
        let aConsistentJobB =
            async { return someConsistentResult }

        let funcInconsistent,funcConsistentA,funcConsistentB =
            serverWithNoHistoryInfoBecauseIrrelevantToThisTest "anInconsistentJob" anInconsistentJob,
            serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aConsistentJobA" aConsistentJobA,
            serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aConsistentJobB" aConsistentJobB

        let settings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                            ConsistencyConfig =
                                NumberOfConsistentResponsesRequired numberOfConsistentResponsesToBeConsideredSafe; }
        let consistencyGuardClient =
            FaultTolerantParallelClient<string, SomeSpecificException>
                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test

        let dataRetreived =
            consistencyGuardClient
                .Query settings
                       [ funcInconsistent; funcConsistentA; funcConsistentB; ]
                    |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo(someConsistentResult))

        let dataRetreived =
            consistencyGuardClient
                .Query settings
                       [ funcConsistentA; funcInconsistent; funcConsistentB; ]
                    |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo(someConsistentResult))

        let dataRetreived =
            consistencyGuardClient
                .Query settings
                       [ funcConsistentA; funcConsistentB; funcInconsistent; ]
                           |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo(someConsistentResult))

    [<Test>]
    member __.``consistency precondition > 0``() =
        let invalidSettings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries()
                                    with ConsistencyConfig = NumberOfConsistentResponsesRequired 0u; }
        let dummyArg = ()
        let dummyServers =
            [ serverWithNoHistoryInfoBecauseIrrelevantToThisTest "dummyServerName" (async { return () }) ]
        Assert.Throws<ArgumentException>(fun _ ->
            (FaultTolerantParallelClient<string, SomeSpecificException>
                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test)
                    .Query invalidSettings dummyServers
                        |> Async.RunSynchronously
                            |> ignore ) |> ignore

    [<Test>]
    member __.``consistency precondition > funcs``() =
        let numberOfConsistentResponsesToBeConsideredSafe = 3u
        let settings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                            ConsistencyConfig =
                                NumberOfConsistentResponsesRequired numberOfConsistentResponsesToBeConsideredSafe; }

        let someResult = 1

        let aJob1 =
            async { return someResult }
        let aJob2 =
            async { return someResult }

        let func1,func2 = serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob1" aJob1,
                          serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob2" aJob2

        let client = FaultTolerantParallelClient<string, SomeSpecificException>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test

        Assert.Throws<ArgumentException>(fun _ ->
            client.Query
                settings
                [ func1; func2 ]
                    |> Async.RunSynchronously
                        |> ignore ) |> ignore

    [<Test>]
    member __.``if consistency is not found, throws inconsistency exception``() =
        let numberOfConsistentResponsesToBeConsideredSafe = 3u
        let settings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             ConsistencyConfig = NumberOfConsistentResponsesRequired numberOfConsistentResponsesToBeConsideredSafe; }

        let mostConsistentResult = 1
        let someOtherResultA = 2
        let someOtherResultB = 3

        let aJob1 =
            async { return someOtherResultA }
        let aJob2 =
            async { return mostConsistentResult }
        let aJob3 =
            async { return someOtherResultB }
        let aJob4 =
            async { return mostConsistentResult }

        let func1,func2,func3,func4 = serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob1" aJob1,
                                      serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob2" aJob2,
                                      serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob3" aJob3,
                                      serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob4" aJob4

        let client = FaultTolerantParallelClient<string, SomeSpecificException>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test

        let inconsistencyEx = Assert.Throws<ResultInconsistencyException>(fun _ ->
                                  client.Query
                                      settings
                                      [ func1; func2; func3; func4 ]
                                          |> Async.RunSynchronously
                                          |> ignore )
        Assert.That(inconsistencyEx.Message, Is.StringContaining("received: 4, consistent: 2, required: 3"))

    [<Test>]
    member __.``retries at least once if all fail``() =
        let mutable count1 = 0
        let aJob1 = async {
            count1 <- count1 + 1
            if (count1 = 1) then
                raise SomeException
            return 0
        }
        let mutable count2 = 0
        let aJob2 = async {
            count2 <- count2 + 1
            if (count2 = 1) then
                raise SomeException
            return 0
        }

        let func1,func2 = serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob1" aJob1,
                          serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob2" aJob2

        let settings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                            NumberOfRetries = 1u
                            NumberOfRetriesForInconsistency = 0u }

        let client = FaultTolerantParallelClient<string, SomeException>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        client.Query settings
                     [ func1; func2 ]
            |> Async.RunSynchronously
            // enough to know that it doesn't throw
            |> ignore

    [<Test>]
    member __.``it retries before throwing inconsistency exception``() =
        let numberOfConsistentResponsesToBeConsideredSafe = 3u

        let mostConsistentResult = 1
        let someOtherResultA = 2
        let someOtherResultB = 3

        let aJob1 =
            async { return someOtherResultA }
        let aJob2 =
            async { return mostConsistentResult }

        let mutable countA = 0
        let aJob3whichGetsConsistentAtSecondTry = async {
            countA <- countA + 1
            if (countA = 1) then
                return someOtherResultB
            else
                return mostConsistentResult
        }

        let mutable countB = 0
        let aJob3whichGetsConsistentAtThirdTry = async {
            countB <- countB + 1
            if (countB < 3) then
                return someOtherResultB
            else
                return mostConsistentResult
        }
        let aJob4 =
            async { return mostConsistentResult }

        let settings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                            ConsistencyConfig =
                                NumberOfConsistentResponsesRequired numberOfConsistentResponsesToBeConsideredSafe;
                            NumberOfRetries = 0u;
                            NumberOfRetriesForInconsistency = 1u }

        let client = FaultTolerantParallelClient<string, SomeSpecificException>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test

        let func1,func2,func3whichGetsConsistentAtSecondTry,func3whichGetsConsistentAtThirdTry,func4 =
            serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob1" aJob1,
            serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob2" aJob2,
            serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob3-2" aJob3whichGetsConsistentAtSecondTry,
            serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob3-3" aJob3whichGetsConsistentAtThirdTry,
            serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob4" aJob4

        client.Query
            settings
            [ func1; func2; func3whichGetsConsistentAtSecondTry; func4 ]
                |> Async.RunSynchronously
                |> ignore

        let inconsistencyEx = Assert.Throws<ResultInconsistencyException>(fun _ ->
                                  client.Query
                                      settings
                                      [ func1; func2; func3whichGetsConsistentAtThirdTry; func4 ]
                                          |> Async.RunSynchronously
                                          |> ignore )
        Assert.That(inconsistencyEx.Message, Is.StringContaining("received: 4, consistent: 2, required: 3"))

    [<Test>]
    member __.``using an average func instead of consistency``() =
        let job1 =
            async { return 1 }
        let job2 =
            async { return 5 }
        let job3 =
            async { return 6 }

        let funcs = [ serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job1" job1
                      serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job2" job2
                      serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job3" job3 ]

        let settings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                            NumberOfMinimumParallelJobs = uint32 funcs.Length
                            NumberOfMaximumParallelJobs = uint32 funcs.Length
                            ConsistencyConfig =
                                AverageBetweenResponses (uint32 funcs.Length,
                                                         (fun (list:List<int>) ->
                                                             list.Sum() / list.Length
                                                         )); }

        let client = FaultTolerantParallelClient<string, SomeSpecificException>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test

        let result = client.Query
                         settings
                         funcs
                             |> Async.RunSynchronously

        Assert.That(result, Is.EqualTo ((1+5+6)/3))

    [<Test>]
    member __.``ordering: chooses server with no faults first``() =
        let someResult1 = 1
        let someResult2 = 2
        let fault = Some { TypeFullName = typeof<Exception>.FullName; Message = "some err" }
        let server1 = { HistoryInfo = Some ({ Fault = fault; TimeSpan = TimeSpan.FromSeconds 1.0 })
                        Identifier = "server1"; Retrieval = async { return someResult1 } }
        let server2 = { HistoryInfo = Some ({ Fault = None; TimeSpan = TimeSpan.FromSeconds 2.0 })
                        Identifier = "server2"; Retrieval = async { return someResult2 } }
        let dataRetreived = (FaultTolerantParallelClient<string,DummyIrrelevantToThisTestException>
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
    member __.``ordering: chooses fastest fail-server option first``() =
        let someResult1 = 1
        let someResult2 = 2
        let fault = Some { TypeFullName = typeof<Exception>.FullName; Message = "some err" }
        let server1,server2 = { HistoryInfo = Some { Fault = fault; TimeSpan = TimeSpan.FromSeconds 2.0 };
                                Identifier = "server1"; Retrieval = async { return someResult1 } },
                              { HistoryInfo = Some { Fault = fault; TimeSpan = TimeSpan.FromSeconds 1.0 };
                                Identifier = "server2"; Retrieval = async { return someResult2 } }
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
    member __.``ordering: chooses server with no faults over servers with no history``() =
        let someResult1 = 1
        let someResult2 = 2
        let server1 = { HistoryInfo = Some ({ Fault = None; TimeSpan = TimeSpan.FromSeconds 1.0 })
                        Identifier = "server1"; Retrieval = async { return someResult1 } }
        let server2 = { HistoryInfo = None
                        Identifier = "server2"; Retrieval = async { return someResult2 } }
        let dataRetreived = (FaultTolerantParallelClient<string, DummyIrrelevantToThisTestException>
                                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test).Query
                                (FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries())
                                [ server1; server2 ]
                                    |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo someResult1)

        // same but different order
        let dataRetreived = (FaultTolerantParallelClient<string, DummyIrrelevantToThisTestException>
                                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test).Query
                                (FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries())
                                [ server2; server1 ]
                                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo someResult1)

    [<Test>]
    member __.``ordering: chooses server with no history before servers with faults in their history``() =
        let someResult1 = 1
        let someResult2 = 2
        let fault = Some { TypeFullName = typeof<Exception>.FullName; Message = "some err" }
        let server1 = { HistoryInfo = Some ({ Fault = fault; TimeSpan = TimeSpan.FromSeconds 1.0 })
                        Identifier = "server1"; Retrieval = async { return someResult1 } }
        let server2 = { HistoryInfo = None
                        Identifier = "server2"; Retrieval = async { return someResult2 } }
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
    member __.``ordering: chooses server with no history before any other servers, in analysis(non fast) mode``() =
        let someResult1 = 1
        let someResult2 = 2
        let someResult3 = 3
        let fault = Some { TypeFullName = typeof<Exception>.FullName; Message = "some err" }
        let server1 = { HistoryInfo = Some ({ Fault = fault; TimeSpan = TimeSpan.FromSeconds 1.0 })
                        Identifier = "server1"; Retrieval = async { return someResult1 } }
        let server2 = { HistoryInfo = None
                        Identifier = "server2"; Retrieval = async { return someResult2 } }
        let server3 = { HistoryInfo = Some ({ Fault = None; TimeSpan = TimeSpan.FromSeconds 1.0 })
                        Identifier = "server3"; Retrieval = async { return someResult3 } }
        let dataRetreived = (FaultTolerantParallelClient<string, DummyIrrelevantToThisTestException>
                                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test).Query
                                { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries()
                                      with Mode = Mode.Analysis }
                                [ server1; server2; server3 ]
                                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo someResult2)

        // same but different order
        let dataRetreived = (FaultTolerantParallelClient<string, DummyIrrelevantToThisTestException>
                                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test).Query
                                { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries()
                                      with Mode = Mode.Analysis }
                                [ server3; server2; server1 ]
                                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo someResult2)

    [<Test>]
    member __.``ordering: leaves one third of servers queried for faulty ones in analysis(non-fast) mode``() =
        let someResult1 = 1
        let someResult2 = 2
        let someResult3 = 3
        let someResult4 = 4
        let server1,server2,server3 = { HistoryInfo = Some { Fault = None; TimeSpan = TimeSpan.FromSeconds 1.0 };
                                        Identifier = "server1"; Retrieval = async { return raise SomeSpecificException} },
                                      { HistoryInfo = Some { Fault = None; TimeSpan = TimeSpan.FromSeconds 2.0 };
                                        Identifier = "server2"; Retrieval = async { return raise SomeSpecificException} },
                                      { HistoryInfo = Some { Fault = None; TimeSpan = TimeSpan.FromSeconds 3.0 };
                                        Identifier = "server3"; Retrieval = async { return someResult3 } }
        let fault = Some { TypeFullName = typeof<Exception>.FullName; Message = "some err" }
        let server4 = { HistoryInfo = Some { Fault = fault; TimeSpan = TimeSpan.FromSeconds 1.0 }
                        Identifier = "server4"; Retrieval = async { return someResult4 } }
        let dataRetreived = (FaultTolerantParallelClient<string, SomeSpecificException>
                                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test).Query
                                { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries()
                                      with Mode = Mode.Analysis }
                                [ server1; server2; server3; server4 ]
                                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo someResult4)

        // same but different order
        let dataRetreived = (FaultTolerantParallelClient<string, SomeSpecificException>
                                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test).Query
                                { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries()
                                      with Mode = Mode.Analysis }
                                [ server4; server3; server2; server1 ]
                                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo someResult4)

    [<Test>]
    member __.``ordering: leaves every 4th position for a non-best server in analysis(non-fast) mode``() =
        let someResult1 = 1
        let someResult2 = 2
        let someResult3 = 3
        let someResult4 = 4
        let someResult5 = 5
        let server1,server2,server3,server4,server5 =
            { HistoryInfo = Some { Fault = None; TimeSpan = TimeSpan.FromSeconds 1.0 }
              Identifier = "server1"; Retrieval = async { return raise SomeSpecificException } },
            { HistoryInfo = Some { Fault = None; TimeSpan = TimeSpan.FromSeconds 2.0 }
              Identifier = "server2"; Retrieval = async { return raise SomeSpecificException } },
            { HistoryInfo = Some { Fault = None; TimeSpan = TimeSpan.FromSeconds 3.0 }
              Identifier = "server3"; Retrieval = async { return raise SomeSpecificException } },
            { HistoryInfo = Some { Fault = None; TimeSpan = TimeSpan.FromSeconds 4.0 }
              Identifier = "server4"; Retrieval = async { return someResult4 } },
            { HistoryInfo = Some { Fault = None; TimeSpan = TimeSpan.FromSeconds 5.0 }
              Identifier = "server5"; Retrieval = async { return someResult5 } }
        let dataRetreived = (FaultTolerantParallelClient<string, SomeSpecificException>
                                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test).Query
                                { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries()
                                      with Mode = Mode.Analysis }
                                [ server1; server2; server3; server4; server5 ]
                                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo someResult5)

        // same but different order
        let dataRetreived = (FaultTolerantParallelClient<string, SomeSpecificException>
                                dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test).Query
                                { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries()
                                      with Mode = Mode.Analysis }
                                [ server5; server4; server3; server2; server1 ]
                                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo someResult5)

    [<Test>]
    member __.``can save server last stat``() =
        let someResult = 1
        let aJob =
            async { return someResult }
        let serverId = "aJob"
        let func = serverWithNoHistoryInfoBecauseIrrelevantToThisTest serverId aJob

        let mutable someFlag = false
        let mutable someTimeStamp = None
        let saveServerLastStat (serverId: string, historyInfo): unit =
            Assert.That(serverId, Is.EqualTo serverId)
            Assert.That(historyInfo.Fault, Is.EqualTo None)
            Assert.That(historyInfo.TimeSpan, Is.GreaterThan TimeSpan.Zero)
            someFlag <- true

        let dataRetreived =
            (FaultTolerantParallelClient<string, DummyIrrelevantToThisTestException>
                                    saveServerLastStat).Query
                (defaultSettingsForNoConsistencyNoParallelismAndNoRetries()) [ func ]
                |> Async.RunSynchronously

        Assert.That(someFlag, Is.EqualTo true)

    [<Test>]
    member __.``can save server last fault``() =
        let aFailingJob: Async<int> =
            async { return raise SomeSpecificException }
        let failingServerName = "aFailingJob"
        let server1 = serverWithNoHistoryInfoBecauseIrrelevantToThisTest failingServerName aFailingJob

        let someResult = 1
        let aJob =
            async { return someResult }
        let server2 = serverWithNoHistoryInfoBecauseIrrelevantToThisTest "aJob" aJob

        let mutable someNonFailingCounter = 0
        let mutable someTotalCounter = 0
        let mutable someTimeStamp = None
        let lockObj = Object()
        let saveServerLastStat (serverId: string, historyInfo): unit =
            lock lockObj (fun _ ->
                match historyInfo.Fault with
                | None ->
                    Assert.That(serverId, Is.Not.EqualTo failingServerName)
                    someNonFailingCounter <- someNonFailingCounter + 1
                | Some fault ->
                    Assert.That(serverId, Is.EqualTo failingServerName)
                    Assert.That(fault.TypeFullName, Is.EqualTo typeof<SomeSpecificException>.FullName)
                Assert.That(historyInfo.TimeSpan, Is.GreaterThan TimeSpan.Zero)
                someTotalCounter <- someTotalCounter + 1
            )

        let dataRetreived =
            (FaultTolerantParallelClient<string, SomeSpecificException>
                                    saveServerLastStat).Query
                (defaultSettingsForNoConsistencyNoParallelismAndNoRetries()) [ server1; server2 ]
                |> Async.RunSynchronously

        Assert.That(someTotalCounter, Is.EqualTo 2)
        Assert.That(someNonFailingCounter, Is.EqualTo 1)

    member private __.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() =
        defaultSettingsForNoConsistencyNoParallelismAndNoRetries()

    static member DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() =
        FaultTolerance().DefaultSettingsForNoConsistencyNoParallelismAndNoRetries()
