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
                client.QueryWithCancellation externalCancellationSource settings allFuncs
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
                client.QueryWithCancellation externalCancellationSource settings allFuncs
                    |> Async.RunSynchronously |> Some

            with
            | ex ->
                let resException = FSharpUtil.FindException<ResourceUnavailabilityException> ex
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
        let cancelSource = new CancellationTokenSource()

        let SomeMethodAsync(cancelToken: CancellationToken): Task<unit> =
            let job = async {
                do! Async.Sleep <| int (someShortTime).TotalMilliseconds
                do! Async.Sleep <| int (someLongTime).TotalMilliseconds
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
                do! Async.Sleep <| int someLongTime.TotalMilliseconds
                return 0
            }
        let longJobThatShouldBeCancelled = async {
            job2started <- true
            let! currentCancellationToken = Async.CancellationToken
            let task = SomeMethodAsync currentCancellationToken
            do! Async.AwaitTask task
            return 0
        }

        let allFuncs = [
                           serverWithNoHistoryInfoBecauseIrrelevantToThisTest "job1" job1
                           serverWithNoHistoryInfoBecauseIrrelevantToThisTest "longJob" longJobThatShouldBeCancelled
                       ]
        let number_of_parallel_jobs_allowed = uint32 allFuncs.Length
        let NUMBER_OF_CONSISTENT_RESULTS = 2u

        let settings = { FaultTolerance.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             NumberOfMaximumParallelJobs = number_of_parallel_jobs_allowed
                             ConsistencyConfig = NumberOfConsistentResponsesRequired NUMBER_OF_CONSISTENT_RESULTS }

        let client = FaultTolerantParallelClient<string, SomeExceptionDuringParallelWork>
                         dummy_func_to_not_save_server_because_it_is_irrelevant_for_this_test
        let task = client.QueryWithCancellation cancelSource settings allFuncs
                         |> Async.StartAsTask

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
    member self.``nested tasks of slower funcs get cancelled after consistent results have been gathered``() =
        self.NestedTasksTest false

        self.NestedTasksTest true


[<TestFixture>]
type DotNetAsyncCancellation() =

    (* NOTE: these tests are not really GWallet tests, but tests around F#&C# async/await&cancelToken behaviours, to
             make me understand better how it works; this means that these tests will never be broken by any code that
             would be introduced in the wallet. If they break, then Microsoft fucked up! haha *)

    [<Test>]
    member __.``assignment after Task.Delay does not await the delay obiously``() =
        let mutable finishedDelay = false
        let SomeMethodAsync(): Task =
            let task = Task.Delay(TimeSpan.FromSeconds 1.0)
            finishedDelay <- true
            task
        let asyncJob = async {
            Assert.That(finishedDelay, Is.EqualTo false, "initial state")
            let task = SomeMethodAsync()
            Assert.That(finishedDelay, Is.EqualTo true, "got the task")
            do! Async.AwaitTask task
            Assert.That(finishedDelay, Is.EqualTo true, "after awaited the task")
        }
        Async.RunSynchronously asyncJob

    [<Test>]
    member __.``assignment when Task.Delay.Continue() awaits the delay``() =
        let mutable finishedDelay = false
        let SomeMethodAsync(): Task =
            Task.Delay(TimeSpan.FromSeconds 1.0).ContinueWith(fun _ ->
                finishedDelay <- true
            )
        let asyncJob = async {
            Assert.That(finishedDelay, Is.EqualTo false, "initial state")
            let task = SomeMethodAsync()
            Assert.That(finishedDelay, Is.EqualTo false, "got the task")
            do! Async.AwaitTask task
            Assert.That(finishedDelay, Is.EqualTo true, "after awaited the task")
        }
        Async.RunSynchronously asyncJob

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it? (1)``() =
        // normal behaviour
        let mutable someCount = 1
        let SomeMethodAsync(): Task =
            Task.Delay(TimeSpan.FromSeconds 2.0).ContinueWith(fun _ ->
                someCount <- someCount + 10
            )
        let asyncJob = async {
            let task = SomeMethodAsync()
            do! Async.AwaitTask task
            someCount <- someCount + 100
        }
        Async.RunSynchronously asyncJob
        Assert.That(someCount, Is.EqualTo 111, "after awaited the task")

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it? (2)``() =
        // cancellation doesn't get propagated to the awaited task if it's already being awaited
        use cancelSource = new CancellationTokenSource()
        let mutable newCount = 1
        let SomeMethodAsync(): Task =
            Task.Delay(TimeSpan.FromSeconds 3.0).ContinueWith(fun _ ->
                newCount <- newCount + 10
            )
        let asyncJob = async {
            let task = SomeMethodAsync()
            do! Async.AwaitTask task
            newCount <- newCount + 100
        }
        let task = Async.StartAsTask (asyncJob, ?cancellationToken = Some cancelSource.Token)
        Thread.Sleep(TimeSpan.FromSeconds 1.0)
        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo false)
        cancelSource.Cancel()
        Thread.Sleep(TimeSpan.FromSeconds 6.0)
        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo true)
        Assert.That(newCount, Is.EqualTo 111, "cancellation didn't work at all")

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it? (3)``() =
        // cancellation works partially because it happens before AwaitTask is called
        use cancelSource = new CancellationTokenSource()
        let mutable newCount = 1
        let SomeMethodAsync(): Task =
            Task.Delay(TimeSpan.FromSeconds 3.0).ContinueWith(fun _ ->
                newCount <- newCount + 10
            )
        let asyncJob = async {
            Thread.Sleep(TimeSpan.FromSeconds 2.0)
            let task = SomeMethodAsync()
            do! Async.AwaitTask task
            newCount <- newCount + 100
        }
        let task = Async.StartAsTask (asyncJob, ?cancellationToken = Some cancelSource.Token)
        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo false)
        cancelSource.Cancel()
        Thread.Sleep(TimeSpan.FromSeconds 6.0)
        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo true)
        Assert.That(newCount, Is.EqualTo 11, "cancellation worked partially")

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it? (4)``() =
        // easy cancellation with an async.sleep
        let mutable newCount = 1
        use cancelSource = new CancellationTokenSource()
        let SomeMethodAsync(): Task =
            Task.Delay(TimeSpan.FromSeconds 2.0).ContinueWith(fun _ ->
                newCount <- newCount + 10
            )
        let asyncJob = async {
            let task = SomeMethodAsync()
            do! Async.AwaitTask task
            do! Async.Sleep <| int (TimeSpan.FromSeconds 2.0).TotalMilliseconds
            newCount <- newCount + 100
        }
        let task = Async.StartAsTask (asyncJob, ?cancellationToken = Some cancelSource.Token)
        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo false)
        Thread.Sleep(TimeSpan.FromSeconds 1.0)
        cancelSource.Cancel()
        Assert.That(newCount, Is.EqualTo 1, "cancelled properly, before waiting")
        Thread.Sleep(TimeSpan.FromSeconds 5.0)
        Assert.That(newCount, Is.EqualTo 11, "cancellation works this way partially too")
        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo true)

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it? (5)``() =
        // immediate cancellation inside async{}
        use cancelSource = new CancellationTokenSource()
        let mutable newCount = 1
        let SomeMethodAsync(): Task =
            Task.Delay(TimeSpan.FromSeconds 2.0).ContinueWith(fun _ ->
                newCount <- newCount + 10
            )
        let asyncJob = async {
            let task = SomeMethodAsync()
            cancelSource.Cancel()
            do! Async.AwaitTask task
            newCount <- newCount + 100
        }
        let task = Async.StartAsTask (asyncJob, ?cancellationToken = Some cancelSource.Token)
        Assert.That(newCount, Is.EqualTo 1, "cancelled properly, before waiting")
        Thread.Sleep(TimeSpan.FromSeconds 3.0)
        Assert.That(newCount, Is.EqualTo 11, "even if cancelled early, the task is still done, after waiting")
        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo true)

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it? (6)``() =
        let mutable newCount = 1
        use cancelSource = new CancellationTokenSource()
        let SomeMethodAsync(): Task =
            Task.Delay(TimeSpan.FromSeconds 1.0).ContinueWith(fun _ ->
                newCount <- newCount + 10
            )
        let asyncJob = async {
            cancelSource.Cancel()
            let task = SomeMethodAsync()
            do! Async.AwaitTask task
            newCount <- newCount + 100
        }
        let task = Async.StartAsTask (asyncJob, ?cancellationToken = Some cancelSource.Token)
        Thread.Sleep(TimeSpan.FromSeconds 3.0)
        Assert.That(newCount, Is.EqualTo 11,
            "even if cancelled before getting the task, task is done but cancelled after that")
        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo true)

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it? (7)``() =
        let mutable newCount = 1
        use cancelSource = new CancellationTokenSource()
        let asyncJob = async {
            cancelSource.Cancel()
            do! Async.Sleep <| int (TimeSpan.FromSeconds 2.0).TotalMilliseconds
            newCount <- newCount + 100
        }
        let task = Async.StartAsTask (asyncJob, ?cancellationToken = Some cancelSource.Token)
        Assert.That(newCount, Is.EqualTo 1, "cancelled properly, before waiting")
        Thread.Sleep(TimeSpan.FromSeconds 3.0)
        Assert.That(newCount, Is.EqualTo 1, "cancelled with no awaitTask, it's properly cancelled too")
        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo true)

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it? (8)``() =
        // immediate cancellation inside task does nothing
        use cancelSource = new CancellationTokenSource()
        let mutable newCount = 1
        let SomeMethodAsync(): Task =
            let task1 = Task.Delay(TimeSpan.FromSeconds 1.0)
            let task2andTask1 = task1.ContinueWith(fun _ ->
                newCount <- newCount + 10
                cancelSource.Cancel()
            )
            let task3andTask2andTask2 = task2andTask1.ContinueWith(fun _ ->
                Task.Delay(TimeSpan.FromSeconds 1.0)
            )
            let allTasks = task3andTask2andTask2.ContinueWith(fun (_: Task) ->
                newCount <- newCount + 100
            )
            allTasks
        let asyncJob = async {
            let task = SomeMethodAsync()
            do! Async.AwaitTask task
            newCount <- newCount + 1000
        }
        let task = Async.StartAsTask (asyncJob, ?cancellationToken = Some cancelSource.Token)
        Assert.That(newCount, Is.EqualTo 1, "not cancelled yet, before waiting")
        Thread.Sleep(TimeSpan.FromSeconds 3.0)
        Assert.That(newCount, Is.EqualTo 1111, "cancelled inside task doesn't really cancel!")
        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo true)

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it? (9)``() =
        // immediate cancellation inside task does nothing
        use cancelSource = new CancellationTokenSource()
        let mutable newCount = 1
        let SomeMethodAsync(): Task =
            let task1 = Task.Delay(TimeSpan.FromSeconds 1.0)
            let task2andTask1 = task1.ContinueWith(fun _ ->
                newCount <- newCount + 10
                cancelSource.Cancel()
            )
            let task3andTask2andTask2 = task2andTask1.ContinueWith(fun _ ->
                Task.Delay(TimeSpan.FromSeconds 1.0)
            )
            let allTasks = task3andTask2andTask2.ContinueWith(fun (_: Task) ->
                newCount <- newCount + 100
            )
            allTasks
        let asyncJob = async {
            let task = SomeMethodAsync()
            do! Async.AwaitTask task
            do! Async.Sleep <| int (TimeSpan.FromSeconds 3.0).TotalMilliseconds
            newCount <- newCount + 1000
        }
        let task = Async.StartAsTask (asyncJob, ?cancellationToken = Some cancelSource.Token)
        Assert.That(newCount, Is.EqualTo 1, "not cancelled yet, before waiting")
        Thread.Sleep(TimeSpan.FromSeconds 6.0)
        Assert.That(newCount, Is.EqualTo 111, "cancelled inside task and async.sleep after awaitTask does work partially")
        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo true)

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it if cancelToken is passed``() =
        use cancelSource = new CancellationTokenSource()
        let mutable newCount = 1
        let SomeMethodAsync(): Task =
            let task1 = Task.Delay(TimeSpan.FromSeconds 1.0)
            let task2andTask1 = task1.ContinueWith(fun _ ->
                newCount <- newCount + 10
                cancelSource.Cancel()
            )
            let task3andTask2andTask2 = task2andTask1.ContinueWith(fun _ ->
                Task.Delay(TimeSpan.FromSeconds 1.0)
            )
            let allTasks = task3andTask2andTask2.ContinueWith(fun (_: Task) ->
                newCount <- newCount + 100
            )
            allTasks
        let asyncJob = async {
            let task = SomeMethodAsync()
            do! Async.AwaitTask task
            do! Async.Sleep <| int (TimeSpan.FromSeconds 3.0).TotalMilliseconds
            newCount <- newCount + 1000
        }
        let task = Async.StartAsTask (asyncJob, ?cancellationToken = Some cancelSource.Token)
        Assert.That(newCount, Is.EqualTo 1, "not cancelled yet, before waiting")
        Thread.Sleep(TimeSpan.FromSeconds 6.0)
        Assert.That(newCount, Is.EqualTo 111, "cancelled inside task and async.sleep after awaitTask does work")
        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo true)

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it if cancelToken is propagated``() =
        use cancelSource = new CancellationTokenSource()
        let mutable newCount = 1
        let SomeMethodAsync(): Task =
            let task1 = Task.Delay(TimeSpan.FromSeconds 1.0, cancelSource.Token)
            let task2andTask1 = task1.ContinueWith((fun _ ->
                newCount <- newCount + 10
                cancelSource.Cancel()
            ), cancelSource.Token)
            let task3andTask2andTask2 = task2andTask1.ContinueWith((fun _ ->
                Task.Delay(TimeSpan.FromSeconds 1.0, cancelSource.Token)
            ), cancelSource.Token)
            let allTasks = task3andTask2andTask2.ContinueWith((fun (_: Task) ->
                newCount <- newCount + 100
            ), cancelSource.Token)
            allTasks
        let asyncJob = async {
            let task = SomeMethodAsync()
            do! Async.AwaitTask task
            do! Async.Sleep <| int (TimeSpan.FromSeconds 3.0).TotalMilliseconds
            newCount <- newCount + 1000
        }
        let task = Async.StartAsTask (asyncJob, ?cancellationToken = Some cancelSource.Token)
        Assert.That(newCount, Is.EqualTo 1, "not cancelled yet, before waiting")
        Thread.Sleep(TimeSpan.FromSeconds 6.0)
        Assert.That(newCount, Is.EqualTo 11, "cancelled inside task propagating token does work")
        Assert.That(task.Exception, Is.Not.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo true)
        Assert.That(task.IsCanceled, Is.EqualTo false)

    [<Test>]
    member __.``cancelling async jobs cancels nested single task awaited inside it if cancelToken is propagated!``() =
        use cancelSource = new CancellationTokenSource()
        let mutable newCount = 1
        let SomeMethodAsync(): Task<unit> =
            let job = async {
                do! Async.Sleep <| int (TimeSpan.FromSeconds 1.0).TotalMilliseconds
                newCount <- newCount + 10
                cancelSource.Cancel()
                do! Async.Sleep <| int (TimeSpan.FromSeconds 1.0).TotalMilliseconds
                newCount <- newCount + 100
            }
            let task = Async.StartAsTask(job, ?cancellationToken = Some cancelSource.Token)
            task
        let asyncJob = async {
            let task = SomeMethodAsync()
            do! Async.AwaitTask task
            do! Async.Sleep <| int (TimeSpan.FromSeconds 3.0).TotalMilliseconds
            newCount <- newCount + 1000
        }
        let task = Async.StartAsTask (asyncJob, ?cancellationToken = Some cancelSource.Token)
        Assert.That(newCount, Is.EqualTo 1, "not cancelled yet, before waiting")
        Thread.Sleep(TimeSpan.FromSeconds 6.0)
        Assert.That(newCount, Is.EqualTo 11, "cancelled inside task propagating token does work")
        Assert.That(task.Exception, Is.Not.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo true)
        Assert.That(task.IsCanceled, Is.EqualTo false)

    [<Test>]
    member __.``cancelling async jobs cancels nested single task awaited inside it if cancelToken is propagated!!``() =
        use cancelSource = new CancellationTokenSource()
        let mutable newCount = 1
        let SomeMethodAsync(cancelToken: CancellationToken): Task<unit> =
            let job = async {
                do! Async.Sleep <| int (TimeSpan.FromSeconds 1.0).TotalMilliseconds
                newCount <- newCount + 10
                cancelSource.Cancel()
                do! Async.Sleep <| int (TimeSpan.FromSeconds 1.0).TotalMilliseconds
                newCount <- newCount + 100
            }
            let task = Async.StartAsTask(job, ?cancellationToken = Some cancelToken)
            task
        let asyncJob = async {
            let! token = Async.CancellationToken
            let task = SomeMethodAsync token
            do! Async.AwaitTask task
            do! Async.Sleep <| int (TimeSpan.FromSeconds 3.0).TotalMilliseconds
            newCount <- newCount + 1000
        }
        let task = Async.StartAsTask (asyncJob, ?cancellationToken = Some cancelSource.Token)
        Assert.That(newCount, Is.EqualTo 1, "not cancelled yet, before waiting")
        Thread.Sleep(TimeSpan.FromSeconds 6.0)
        Assert.That(newCount, Is.EqualTo 11, "cancelled inside task propagating token does work")
        Assert.That(task.Exception, Is.Not.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo true)
        Assert.That(task.IsCanceled, Is.EqualTo false)

    [<Test>]
    member __.``cancel an already disposed cancellation source``() =
        let cancelSource = new CancellationTokenSource()
        cancelSource.Dispose()
        Assert.Throws<ObjectDisposedException>(fun _ ->
            cancelSource.Cancel()
        ) |> ignore

    [<Test>]
    member __.``cancel token of a nested async job is the same as parent's (so F# is awesome at propagating)``() =
        let someRandomNumber = 333
        let nestedAsyncJob (parentCancelToken: CancellationToken) =
            async {
                let! currentCancelToken = Async.CancellationToken
                Assert.That(currentCancelToken, Is.Not.EqualTo CancellationToken.None, "!=None1")
                Assert.That(currentCancelToken.GetHashCode(), Is.EqualTo (parentCancelToken.GetHashCode()), "hashcode1")
                //Assert.That(Object.ReferenceEquals(currentCancelToken, parentCancelToken), Is.EqualTo true, "obj.ref=1")
                Assert.That(currentCancelToken, Is.EqualTo parentCancelToken, "equality1")
                return someRandomNumber
            }
        let rootAsyncJob =
            async {
                let! currentRootCancelToken = Async.CancellationToken
                let! resultFromNestedJob = nestedAsyncJob currentRootCancelToken
                Assert.That(resultFromNestedJob, Is.EqualTo someRandomNumber)
            }
        Async.RunSynchronously rootAsyncJob

        let nestedAsyncJobWithSomeSynchronousExecution (parentCancelToken: CancellationToken) =
            Console.WriteLine "foobarbaz"
            async {
                let! currentCancelToken = Async.CancellationToken
                Assert.That(currentCancelToken, Is.Not.EqualTo CancellationToken.None, "!=None2")
                Assert.That(currentCancelToken.GetHashCode(), Is.EqualTo (parentCancelToken.GetHashCode()), "hashcode2")
                //Assert.That(Object.ReferenceEquals(currentCancelToken, parentCancelToken), Is.EqualTo true, "obj.ref=2")
                Assert.That(currentCancelToken, Is.EqualTo parentCancelToken, "equality2")
                return someRandomNumber
            }
        let rootAsyncJob2 =
            async {
                let! currentRootCancelToken = Async.CancellationToken
                let! resultFromNestedJob = nestedAsyncJobWithSomeSynchronousExecution currentRootCancelToken
                Assert.That(resultFromNestedJob, Is.EqualTo someRandomNumber)
            }
        Async.RunSynchronously rootAsyncJob2

        use cancelSource = new CancellationTokenSource()
        let nestedAsyncJobWhenLaunchedWithASource (parentCancelToken: CancellationToken) =
            async {
                let! currentCancelToken = Async.CancellationToken
                Assert.That(currentCancelToken, Is.Not.EqualTo CancellationToken.None, "!=None3")
                Assert.That(currentCancelToken.GetHashCode(), Is.EqualTo (parentCancelToken.GetHashCode()), "hashcode3")
                //Assert.That(Object.ReferenceEquals(currentCancelToken, parentCancelToken), Is.EqualTo true, "obj.ref=3")
                Assert.That(currentCancelToken, Is.EqualTo parentCancelToken, "equality3")

                Assert.That(cancelSource.Token, Is.Not.EqualTo CancellationToken.None, "!=None4")
                Assert.That(currentCancelToken.GetHashCode(), Is.EqualTo (cancelSource.Token.GetHashCode()), "hashcode4")
                //Assert.That(Object.ReferenceEquals(currentCancelToken, parentCancelToken), Is.EqualTo true, "obj.ref=4")
                Assert.That(currentCancelToken, Is.EqualTo cancelSource.Token, "equality4")
                return someRandomNumber
            }
        let rootAsyncJob3 =
            async {
                let! currentRootCancelToken = Async.CancellationToken

                Assert.That(currentRootCancelToken, Is.Not.EqualTo CancellationToken.None, "!=None5")
                Assert.That(currentRootCancelToken.GetHashCode(), Is.EqualTo (cancelSource.Token.GetHashCode()), "hashcode5")
                //Assert.That(Object.ReferenceEquals(currentCancelToken, parentCancelToken), Is.EqualTo true, "obj.ref=5")
                Assert.That(currentRootCancelToken, Is.EqualTo cancelSource.Token, "equality5")

                let! resultFromNestedJob = nestedAsyncJobWithSomeSynchronousExecution currentRootCancelToken
                Assert.That(resultFromNestedJob, Is.EqualTo someRandomNumber)
            }
        let task = Async.StartAsTask(rootAsyncJob3, ?cancellationToken = Some cancelSource.Token)
        task.Wait()
        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)

