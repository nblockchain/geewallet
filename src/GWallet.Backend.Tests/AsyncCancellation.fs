namespace GWallet.Backend.Tests

open System
open System.Threading
open System.Threading.Tasks

open GWallet.Backend

open NUnit.Framework

[<TestFixture>]
type AsyncCancellation() =

    let serverWithNoHistoryInfoBecauseIrrelevantToThisTest serverId job =
        { Identifier = serverId; HistoryInfo = None; Retrieval = job; }
    let dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test = (fun _ -> ())

    [<Test>]
    member __.``slower funcs get cancelled after consistent results have been gathered``() =
        let someLongTime = TimeSpan.FromSeconds 1.0

        let mutable longFuncFinishedExecution = false
        let job1 =
            async { return 0 }
        let job2 =
            async { return 0 }
        let longJobThatShouldBeCancelled = async {
            do! Async.Sleep <| int someLongTime.TotalMilliseconds
            longFuncFinishedExecution <- true
            return 0
        }

        let allFuncs = [ serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job1" job1
                         serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job2" job2
                         serverWithNoHistoryInfoBecauseIrrelevantToThisTest "longJob" longJobThatShouldBeCancelled ]
        let number_of_parallel_jobs_allowed = uint32 allFuncs.Length
        let NUMBER_OF_CONSISTENT_RESULTS = 2u

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             NumberOfMaximumParallelJobs = number_of_parallel_jobs_allowed;
                             ConsistencyConfig = NumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS; }

        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        let result = client.Query settings allFuncs
                         |> Async.RunSynchronously

        Assert.That(result, Is.EqualTo 0)

        // we sleep longer than someLongTime, to make sure longFunc is finished
        Thread.Sleep(someLongTime + someLongTime)

        Assert.That(longFuncFinishedExecution, Is.EqualTo false)

    [<Test>]
    member __.``external cancellation source causes TaskCancelledException``() =
        let someLongTime = TimeSpan.FromSeconds 1.0

        let job1 = async {
            do! Async.Sleep <| int someLongTime.TotalMilliseconds
            return 1
        }
        let job2 = async {
            do! Async.Sleep <| int someLongTime.TotalMilliseconds
            return 2
        }

        let allFuncs = [ serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job1" job1
                         serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job2" job2 ]
        let number_of_parallel_jobs_allowed = uint32 allFuncs.Length
        let NUMBER_OF_CONSISTENT_RESULTS = 1u

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             NumberOfMaximumParallelJobs = number_of_parallel_jobs_allowed;
                             ConsistencyConfig = NumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS; }

        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test

        let externalCancellationSource = new CancellationTokenSource()
        externalCancellationSource.Cancel()
        let task = client.QueryWithCancellation settings allFuncs externalCancellationSource
                       |> Async.StartAsTask

        // to let the task start
        Thread.Sleep(TimeSpan.FromSeconds 0.5)

        let result =
            try
                externalCancellationSource.Cancel()
                task.Result |> Some
            with
            | ex ->
                let taskException = FSharpUtil.FindException<TaskCanceledException> ex
                let exDetails = ex.ToString()
                Assert.That(taskException.IsSome, Is.True, exDetails)
                None

        // to make sure the exception happened
        Assert.That(result.IsNone, Is.True)

    [<Test>]
    member __.``external cancellation source can come already cancelled``() =
        let someLongTime = TimeSpan.FromSeconds 1.0

        let externalCancellationSource = new CancellationTokenSource()
        externalCancellationSource.Cancel()

        let job1 = async {
            return 1
        }
        let job2 = async {
            return 2
        }

        let allFuncs = [ serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job1" job1
                         serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job2" job2 ]
        let number_of_parallel_jobs_allowed = uint32 allFuncs.Length
        let NUMBER_OF_CONSISTENT_RESULTS = 1u

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             NumberOfMaximumParallelJobs = number_of_parallel_jobs_allowed;
                             ConsistencyConfig = NumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS; }

        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test

        let result =
            try
                client.QueryWithCancellation settings allFuncs externalCancellationSource
                    |> Async.RunSynchronously |> Some

            with
            | ex ->
                let taskException = FSharpUtil.FindException<TaskCanceledException> ex
                let exDetails = ex.ToString()
                Assert.That(taskException.IsSome, Is.True, exDetails)
                None

        // to make sure the exception happened
        Assert.That(result.IsNone, Is.True)

    [<Test>]
    member __.``external cancellation source can come already disposed``() =
        let someLongTime = TimeSpan.FromSeconds 1.0

        let externalCancellationSource = new CancellationTokenSource()
        externalCancellationSource.Dispose()

        let job1 = async {
            return 1
        }
        let job2 = async {
            return 2
        }

        let allFuncs = [ serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job1" job1
                         serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job2" job2 ]
        let number_of_parallel_jobs_allowed = uint32 allFuncs.Length
        let NUMBER_OF_CONSISTENT_RESULTS = 1u

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             NumberOfMaximumParallelJobs = number_of_parallel_jobs_allowed;
                             ConsistencyConfig = NumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS; }

        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test

        let result =
            try
                client.QueryWithCancellation settings allFuncs externalCancellationSource
                    |> Async.RunSynchronously |> Some

            with
            | ex ->
                let resException = FSharpUtil.FindException<ResourceUnavailabilityException> ex
                let exDetails = ex.ToString()
                Assert.That(resException.IsSome, Is.True, exDetails)
                None

        // to make sure the exception happened
        Assert.That(result.IsNone, Is.True)


    [<Test>]
    member __.``external cancellation source can come already cancelled&disposed``() =
        let someLongTime = TimeSpan.FromSeconds 1.0

        let externalCancellationSource = new CancellationTokenSource()
        externalCancellationSource.Cancel()
        externalCancellationSource.Dispose()

        let job1 = async {
            return 1
        }
        let job2 = async {
            return 2
        }

        let allFuncs = [ serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job1" job1
                         serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job2" job2 ]
        let number_of_parallel_jobs_allowed = uint32 allFuncs.Length
        let NUMBER_OF_CONSISTENT_RESULTS = 1u

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             NumberOfMaximumParallelJobs = number_of_parallel_jobs_allowed;
                             ConsistencyConfig = NumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS; }

        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test

        let result =
            try
                client.QueryWithCancellation settings allFuncs externalCancellationSource
                    |> Async.RunSynchronously |> Some

            with
            | ex ->
                let resException = FSharpUtil.FindException<ResourceUnavailabilityException> ex
                let exDetails = ex.ToString()
                Assert.That(resException.IsSome, Is.True, exDetails)
                None

        // to make sure the exception happened
        Assert.That(result.IsNone, Is.True)

