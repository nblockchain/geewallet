namespace GWallet.Backend.Tests

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks

open Fsdk
open Fsdk.FSharpUtil

open GWallet.Backend

open NUnit.Framework

[<TestFixture>]
type FaultTolerantParallelClientAsyncCancellation() =

    let dummy_connection_type = { Encrypted = false; Protocol = Http }
    let serverWithNoHistoryInfoBecauseIrrelevantToThisTest serverId job =
        {
            Details =
                {
                    ServerInfo =
                        {
                            NetworkPath = serverId
                            ConnectionType = dummy_connection_type
                        }
                    CommunicationHistory = None
                }
            Retrieval = fun _timeout -> job
        }
    let dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test = (fun _ _ -> ())

    [<Test>]
    member __.``slower funcs get canceled after consistent results have been gathered``() =
        let someLongTime = TimeSpan.FromSeconds 1.0

        let mutable longFuncFinishedExecution = false
        let job1 =
            async { return 0 }
        let job2 =
            async { return 0 }
        let longJobThatShouldBeCanceled = async {
            do! SleepSpan someLongTime
            longFuncFinishedExecution <- true
            return 0
        }

        let allFuncs = [ serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job1" job1
                         serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job2" job2
                         serverWithNoHistoryInfoBecauseIrrelevantToThisTest "longJob" longJobThatShouldBeCanceled ]
        let number_of_parallel_jobs_allowed = uint32 allFuncs.Length
        let NUMBER_OF_CONSISTENT_RESULTS = 2u

        let consistencyCfg = SpecificNumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS |> Some
        let settings =
            {
                FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries consistencyCfg with
                    NumberOfParallelJobsAllowed = number_of_parallel_jobs_allowed
            }

        let client = FaultTolerantParallelClient<ServerDetails, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        let result = client.Query settings allFuncs
                         |> Async.RunSynchronously

        Assert.That(result, Is.EqualTo 0)

        // we sleep longer than someLongTime, to make sure longFunc is finished
        Thread.Sleep(someLongTime + someLongTime)

        Assert.That(longFuncFinishedExecution, Is.EqualTo false)

    [<Test>]
    member __.``quick cancellation from external cancellation source on long jobs causes TaskCanceledException``() =
        let someLongTime = TimeSpan.FromSeconds 1.0

        let job1 = async {
            do! SleepSpan someLongTime
            return 1
        }
        let job2 = async {
            do! SleepSpan someLongTime
            return 2
        }

        let allFuncs = [ serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job1" job1
                         serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job2" job2 ]
        let number_of_parallel_jobs_allowed = uint32 allFuncs.Length
        let NUMBER_OF_CONSISTENT_RESULTS = 1u

        let consistencyCfg = SpecificNumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS |> Some
        let settings =
            {
                FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries consistencyCfg with
                    NumberOfParallelJobsAllowed = number_of_parallel_jobs_allowed
            }

        let client = FaultTolerantParallelClient<ServerDetails, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test

        let externalCancellationSource = new CustomCancelSource()
        let task = client.QueryWithCancellation externalCancellationSource settings allFuncs
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
    member __.``external cancellation source can come already canceled``() =
        let externalCancellationSource = new CustomCancelSource()
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

        let consistencyCfg = SpecificNumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS |> Some
        let settings =
            {
                FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries consistencyCfg with
                    NumberOfParallelJobsAllowed = number_of_parallel_jobs_allowed
            }

        let client = FaultTolerantParallelClient<ServerDetails, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test

        let result =
            try
                client.QueryWithCancellation externalCancellationSource settings allFuncs
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
        let externalCancellationSource = new CustomCancelSource()
        (externalCancellationSource:>IDisposable).Dispose()

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

        let consistencyCfg = SpecificNumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS |> Some
        let settings =
            {
                FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries consistencyCfg with
                    NumberOfParallelJobsAllowed = number_of_parallel_jobs_allowed
            }

        let client = FaultTolerantParallelClient<ServerDetails, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test

        let result =
            try
                client.QueryWithCancellation externalCancellationSource settings allFuncs
                    |> Async.RunSynchronously |> Some

            with
            | ex ->
                let resException = FSharpUtil.FindException<TaskCanceledException> ex
                let exDetails = ex.ToString()
                Assert.That(resException.IsSome, Is.True, exDetails)
                None

        // to make sure the exception happened
        Assert.That(result.IsNone, Is.True)


    [<Test>]
    member __.``external cancellation source can come already canceled&disposed``() =
        let externalCancellationSource = new CustomCancelSource()
        externalCancellationSource.Cancel()
        (externalCancellationSource:>IDisposable).Dispose()

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

        let consistencyCfg = SpecificNumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS |> Some
        let settings =
            {
                FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries consistencyCfg with
                    NumberOfParallelJobsAllowed = number_of_parallel_jobs_allowed
            }

        let client = FaultTolerantParallelClient<ServerDetails, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test

        let result =
            try
                client.QueryWithCancellation externalCancellationSource settings allFuncs
                    |> Async.RunSynchronously |> Some

            with
            | ex ->
                let resException = FSharpUtil.FindException<TaskCanceledException> ex
                let exDetails = ex.ToString()
                Assert.That(resException.IsSome, Is.True, exDetails)
                None

        // to make sure the exception happened
        Assert.That(result.IsNone, Is.True)


    member __.NestedTasksTest(propagateToken: bool) =

        let someShortTime = TimeSpan.FromSeconds 0.5
        let someLongTime = TimeSpan.FromSeconds 1.0
        let mutable longFuncFinishedExecution = false
        let mutable job1started = false
        let mutable job2started = false
        let cancelSource = new CustomCancelSource()

        let SomeMethodAsync(cancelToken: CancellationToken): Task<unit> =
            let job = async {
                do! SleepSpan someShortTime
                do! SleepSpan someLongTime
                longFuncFinishedExecution <- true
            }
            let task =
                if propagateToken then
                    Async.StartAsTask(job, ?cancellationToken = Some cancelToken)
                else
                    Async.StartAsTask job
            task

        let job1 =
            async {
                job1started <- true
                do! SleepSpan someLongTime
                return 0
            }
        let longJobThatShouldBeCanceled = async {
            job2started <- true
            let! currentCancellationToken = Async.CancellationToken
            let task = SomeMethodAsync currentCancellationToken
            do! Async.AwaitTask task
            return 0
        }

        let allFuncs = [
                           serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job1" job1
                           serverWithNoHistoryInfoBecauseIrrelevantToThisTest "longJob" longJobThatShouldBeCanceled
                       ]
        let number_of_parallel_jobs_allowed = uint32 allFuncs.Length
        let NUMBER_OF_CONSISTENT_RESULTS = 2u

        let consistencyCfg = SpecificNumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS |> Some
        let settings =
            {
                FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries consistencyCfg with
                    NumberOfParallelJobsAllowed = number_of_parallel_jobs_allowed
            }

        let client = FaultTolerantParallelClient<ServerDetails, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        client.QueryWithCancellation cancelSource settings allFuncs
        |> Async.StartAsTask
        |> ignore<Task<int>>

        Assert.That(longFuncFinishedExecution, Is.EqualTo false)
        Thread.Sleep someShortTime

        Assert.That(job1started, Is.EqualTo true)
        Assert.That(job2started, Is.EqualTo true)
        cancelSource.Cancel()

        // we sleep longer than someLongTime, to make sure longFunc is finished
        Thread.Sleep(someLongTime + someLongTime + someLongTime + someLongTime)

        if propagateToken then
            Assert.That(longFuncFinishedExecution, Is.EqualTo false, "propagation=true")
        else
            Assert.That(longFuncFinishedExecution, Is.EqualTo true, "propagation=false")

    [<Test>]
    member self.``nested tasks of slower funcs get canceled after consistent results have been gathered``() =
        self.NestedTasksTest false

        self.NestedTasksTest true

    [<Test>]
    member self.``FSharpUtil.WithTimeout works with cancellation`` () =
        let jobTime = TimeSpan.FromSeconds 10.0
        let timeoutTime = TimeSpan.FromSeconds 5.0

        let job = async {
            do! SleepSpan jobTime
            return 1
        }

        let externalCancellationSource = new CancellationTokenSource()
        let jobWithTimeout = FSharpUtil.WithTimeout timeoutTime job

        let stopWatch = Stopwatch()
        stopWatch.Start()
        let task = Async.StartAsTask(jobWithTimeout, TaskCreationOptions.None, externalCancellationSource.Token)

        // to let the task start
        Thread.Sleep (TimeSpan.FromSeconds 1.0)

        externalCancellationSource.Cancel()

        let result =
            try
                try
                    task.Result
                        |> SuccessfulValue
                with
                | ex ->
                    if (FSharpUtil.FindException<TaskCanceledException> ex).IsSome then
                        FailureResult ex
                    else
                        reraise()
            finally
                stopWatch.Stop()

        match result with
        | SuccessfulValue _ -> Assert.Fail "should have been canceled"
        | _ -> ()

        Assert.That(stopWatch.Elapsed, Is.LessThan timeoutTime)

    [<Test>]
    member __.``cancellationSource is *NOT* disposed after FaultTolerantParallelClient finishes executing``() =
        let externalCancellationSource = new CustomCancelSource()

        let job1 =
            async { return 0 }
        let job2 =
            async { return 0 }

        let allFuncs = [ serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job1" job1
                         serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job2" job2 ]
        let number_of_parallel_jobs_allowed = uint32 allFuncs.Length
        let NUMBER_OF_CONSISTENT_RESULTS = 2u

        let consistencyCfg = SpecificNumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS |> Some
        let settings =
            {
                FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries consistencyCfg with
                    NumberOfParallelJobsAllowed = number_of_parallel_jobs_allowed
            }

        let client = FaultTolerantParallelClient<ServerDetails, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        let result = client.QueryWithCancellation externalCancellationSource settings allFuncs
                         |> Async.RunSynchronously

        Assert.That(result, Is.EqualTo 0)

        // doesn't throw ObjectDisposedException
        externalCancellationSource.Cancel()

