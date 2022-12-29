namespace GWallet.Backend.Tests.Unit

open System
open System.Diagnostics

open NUnit.Framework

open GWallet.Backend

[<TestFixture>]
type AsyncExtensions() =

    [<Test>]
    member __.``basic test for WhenAny``() =
        let shortJobRes = 1
        let shortTime = TimeSpan.FromSeconds 1.
        let shortJob = async {
            do! Async.Sleep (int shortTime.TotalMilliseconds)
            return shortJobRes
        }

        let longJobRes = 2
        let longTime = TimeSpan.FromSeconds 10.
        let longJob = async {
            do! Async.Sleep (int longTime.TotalMilliseconds)
            return longJobRes
        }

        let stopWatch = Stopwatch.StartNew()
        let res1 =
            FSharpUtil.AsyncExtensions.WhenAny [longJob; shortJob]
            |> Async.RunSynchronously
        Assert.That(res1, Is.EqualTo shortJobRes)
        Assert.That(stopWatch.Elapsed, Is.LessThan longTime)
        stopWatch.Stop()

        let stopWatch = Stopwatch.StartNew()
        let res2 =
            FSharpUtil.AsyncExtensions.WhenAny [shortJob; longJob]
            |> Async.RunSynchronously
        Assert.That(res2, Is.EqualTo shortJobRes)
        Assert.That(stopWatch.Elapsed, Is.LessThan longTime)
        stopWatch.Stop()

    [<Test>]
    member __.``basic test for Async.Choice``() =
        let shortTime = TimeSpan.FromSeconds 1.
        let shortFailingJob = async {
            do! Async.Sleep (int shortTime.TotalMilliseconds)
            return None
        }

        let shortSuccessfulJobRes = 2
        let shortSuccessfulJob = async {
            do! Async.Sleep (int shortTime.TotalMilliseconds + int shortTime.TotalMilliseconds)
            return Some shortSuccessfulJobRes
        }

        let longJobRes = 3
        let longTime = TimeSpan.FromSeconds 10.
        let longJob = async {
            do! Async.Sleep (int longTime.TotalMilliseconds)
            return Some longJobRes
        }

        let stopWatch = Stopwatch.StartNew()
        let res1 =
            Async.Choice [longJob; shortFailingJob; shortSuccessfulJob]
            |> Async.RunSynchronously
        Assert.That(res1, Is.EqualTo (Some shortSuccessfulJobRes))
        Assert.That(stopWatch.Elapsed, Is.LessThan longTime, "time#1")
        stopWatch.Stop()

        let stopWatch = Stopwatch.StartNew()
        let res2 =
            Async.Choice [longJob; shortSuccessfulJob; shortFailingJob]
            |> Async.RunSynchronously
        Assert.That(res2, Is.EqualTo (Some shortSuccessfulJobRes))
        Assert.That(stopWatch.Elapsed, Is.LessThan longTime, "time#2")
        stopWatch.Stop()

        let stopWatch = Stopwatch.StartNew()
        let res3 =
            Async.Choice [shortFailingJob; longJob; shortSuccessfulJob]
            |> Async.RunSynchronously
        Assert.That(res3, Is.EqualTo (Some shortSuccessfulJobRes))
        Assert.That(stopWatch.Elapsed, Is.LessThan longTime, "time#3")
        stopWatch.Stop()

        let stopWatch = Stopwatch.StartNew()
        let res4 =
            Async.Choice [shortFailingJob; shortSuccessfulJob; longJob]
            |> Async.RunSynchronously
        Assert.That(res4, Is.EqualTo (Some shortSuccessfulJobRes))
        Assert.That(stopWatch.Elapsed, Is.LessThan longTime, "time#4")
        stopWatch.Stop()

        let stopWatch = Stopwatch.StartNew()
        let res5 =
            Async.Choice [shortSuccessfulJob; longJob; shortFailingJob]
            |> Async.RunSynchronously
        Assert.That(res5, Is.EqualTo (Some shortSuccessfulJobRes))
        Assert.That(stopWatch.Elapsed, Is.LessThan longTime, "time#5")
        stopWatch.Stop()

        let stopWatch = Stopwatch.StartNew()
        let res6 =
            Async.Choice [shortSuccessfulJob; shortFailingJob; longJob]
            |> Async.RunSynchronously
        Assert.That(res6, Is.EqualTo (Some shortSuccessfulJobRes))
        Assert.That(stopWatch.Elapsed, Is.LessThan longTime, "time#6")
        stopWatch.Stop()

    [<Test>]
    member __.``basic test for WhenAnyAndAll``() =
        let lockObj = Object()
        let mutable asyncJobsPerformedCount = 0

        let shortJobRes = 1
        let shortTime = TimeSpan.FromSeconds 2.
        let shortJob = async {
            lock lockObj (fun _ -> asyncJobsPerformedCount <- asyncJobsPerformedCount + 1)
            do! Async.Sleep (int shortTime.TotalMilliseconds)
            return shortJobRes
        }

        let longJobRes = 2
        let longTime = TimeSpan.FromSeconds 3.
        let longJob = async {
            lock lockObj (fun _ -> asyncJobsPerformedCount <- asyncJobsPerformedCount + 1)
            do! Async.Sleep (int longTime.TotalMilliseconds)
            return longJobRes
        }

        let stopWatch = Stopwatch.StartNew()
        let subJobs =
            FSharpUtil.AsyncExtensions.WhenAnyAndAll [longJob; shortJob]
            |> Async.RunSynchronously
        Assert.That(stopWatch.Elapsed, Is.LessThan longTime)
        Assert.That(stopWatch.Elapsed, Is.GreaterThan shortTime)
        let results =
            subJobs |> Async.RunSynchronously
        Assert.That(results.Length, Is.EqualTo 2)
        Assert.That(results.[0], Is.EqualTo longJobRes)
        Assert.That(results.[1], Is.EqualTo shortJobRes)
        stopWatch.Stop()

        Assert.That(asyncJobsPerformedCount, Is.EqualTo 2)

        // the below is to make sure that the jobs don't get executed a second time!
        let stopWatch = Stopwatch.StartNew()
        subJobs
        |> Async.RunSynchronously
        |> ignore<array<int>>
        Assert.That(asyncJobsPerformedCount, Is.EqualTo 2)
        Assert.That(stopWatch.Elapsed, Is.LessThan shortTime)

    [<Test>]
    member __.``AsyncParallel cancels all jobs if there's an exception in one'``() =
        let shortTime = TimeSpan.FromSeconds 2.
        let shortJob = async {
            do! Async.Sleep (int shortTime.TotalMilliseconds)
            return failwith "pepe"
        }

        let longJobRes = 2
        let mutable longJobFinished = false
        let longTime = TimeSpan.FromSeconds 3.
        let longJob = async {
            do! Async.Sleep (int longTime.TotalMilliseconds)
            longJobFinished <- true
            return longJobRes
        }

        let result =
            try
                Async.Parallel [longJob; shortJob]
                |> Async.RunSynchronously |> Some
            with
            | _ -> None

        Assert.That(result, Is.EqualTo None)
        Assert.That(longJobFinished, Is.EqualTo false, "#before")
        Threading.Thread.Sleep(TimeSpan.FromSeconds 7.0)
        Assert.That(longJobFinished, Is.EqualTo false, "#after")

    [<Test>]
    member __.``AsyncChoice cancels slower jobs (all jobs that were not the fastest)``() =
        let shortJobRes = 1
        let shortTime = TimeSpan.FromSeconds 2.
        let shortJob = async {
            do! Async.Sleep (int shortTime.TotalMilliseconds)
            return Some shortJobRes
        }

        let longJobRes = 2
        let mutable longJobFinished = false
        let longTime = TimeSpan.FromSeconds 3.
        let longJob = async {
            do! Async.Sleep (int longTime.TotalMilliseconds)
            longJobFinished <- true
            return Some longJobRes
        }

        let result =
            Async.Choice [longJob; shortJob]
            |> Async.RunSynchronously

        Assert.That(result, Is.EqualTo (Some shortJobRes))
        Assert.That(longJobFinished, Is.EqualTo false, "#before")
        Threading.Thread.Sleep(TimeSpan.FromSeconds 7.0)
        Assert.That(longJobFinished, Is.EqualTo false, "#after")

    [<Test>]
    member __.``AsyncExtensions-WhenAny cancels slower jobs (all jobs that were not the fastest)``() =
        let shortJobRes = 1
        let shortTime = TimeSpan.FromSeconds 2.
        let shortJob = async {
            do! Async.Sleep (int shortTime.TotalMilliseconds)
            return shortJobRes
        }

        let longJobRes = 2
        let mutable longJobFinished = false
        let longTime = TimeSpan.FromSeconds 3.
        let longJob = async {
            do! Async.Sleep (int longTime.TotalMilliseconds)
            longJobFinished <- true
            return longJobRes
        }

        let result =
            FSharpUtil.AsyncExtensions.WhenAny [longJob; shortJob]
            |> Async.RunSynchronously

        Assert.That(result, Is.EqualTo shortJobRes)
        Assert.That(longJobFinished, Is.EqualTo false, "#before")
        Threading.Thread.Sleep(TimeSpan.FromSeconds 7.0)
        Assert.That(longJobFinished, Is.EqualTo false, "#after")

    [<Test>]
    member __.``AsyncExtensions-WhenAnyAndAll doesn't cancel slower jobs``() =
        let shortJobRes = 1
        let shortTime = TimeSpan.FromSeconds 2.
        let shortJob = async {
            do! Async.Sleep (int shortTime.TotalMilliseconds)
            return shortJobRes
        }

        let longJobRes = 2
        let mutable longJobFinished = false
        let longTime = TimeSpan.FromSeconds 3.
        let longJob = async {
            do! Async.Sleep (int longTime.TotalMilliseconds)
            longJobFinished <- true
            return longJobRes
        }

        let jobs =
            FSharpUtil.AsyncExtensions.WhenAnyAndAll [longJob; shortJob]
            |> Async.RunSynchronously
        Assert.That(longJobFinished, Is.EqualTo false, "#before")
        let results = jobs |> Async.RunSynchronously
        Assert.That(results.[0], Is.EqualTo longJobRes)
        Assert.That(results.[1], Is.EqualTo shortJobRes)
        Threading.Thread.Sleep(TimeSpan.FromSeconds 7.0)
        Assert.That(longJobFinished, Is.EqualTo true, "#after")

    [<Test>]
    member __.``Async.MixedParallel2 cancels all jobs if there's an exception in one'``() =
        let shortTime = TimeSpan.FromSeconds 2.
        let shortJob = async {
            do! Async.Sleep (int shortTime.TotalMilliseconds)
            return failwith "pepe"
        }

        let mutable longJobFinished = false
        let longTime = TimeSpan.FromSeconds 3.
        let longJob = async {
            do! Async.Sleep (int longTime.TotalMilliseconds)
            longJobFinished <- true
            return 1
        }

        let result =
            try
                FSharpUtil.AsyncExtensions.MixedParallel2 longJob shortJob
                |> Async.RunSynchronously |> Some
            with
            | _ -> None

        Assert.That(result, Is.EqualTo None)
        Assert.That(longJobFinished, Is.EqualTo false, "#before")
        Threading.Thread.Sleep(TimeSpan.FromSeconds 7.0)
        Assert.That(longJobFinished, Is.EqualTo false, "#after")

    [<Test>]
    member __.``Async.MixedParallel3 cancels all jobs if there's an exception in one'``() =
        let shortTime = TimeSpan.FromSeconds 2.
        let shortJob = async {
            do! Async.Sleep (int shortTime.TotalMilliseconds)
            return failwith "pepe"
        }

        
        let longTime = TimeSpan.FromSeconds 3.

        let mutable longJobFinished = false
        let longJob = async {
            do! Async.Sleep (int longTime.TotalMilliseconds)
            longJobFinished <- true
            return 1
        }

        let mutable longJob2Finished = false
        let longJob2 = async {
            do! Async.Sleep (int longTime.TotalMilliseconds)
            longJobFinished <- true
            return 2.0
        }

        let result =
            try
                FSharpUtil.AsyncExtensions.MixedParallel3 longJob shortJob longJob2
                |> Async.RunSynchronously |> Some
            with
            | _ -> None

        Assert.That(result, Is.EqualTo None)
        Assert.That(longJobFinished, Is.EqualTo false, "#before")
        Assert.That(longJob2Finished, Is.EqualTo false, "#before")
        Threading.Thread.Sleep(TimeSpan.FromSeconds 7.0)
        Assert.That(longJobFinished, Is.EqualTo false, "#after")
        Assert.That(longJob2Finished, Is.EqualTo false, "#before")

    [<Test>]
    member __.``Async.MixedParallel4 cancels all jobs if there's an exception in one'``() =
        let shortTime = TimeSpan.FromSeconds 2.
        let shortJob = async {
            do! Async.Sleep (int shortTime.TotalMilliseconds)
            return failwith "pepe"
        }

        let longTime = TimeSpan.FromSeconds 3.

        let mutable longJobFinished = false
        let longJob = async {
            do! Async.Sleep (int longTime.TotalMilliseconds)
            longJobFinished <- true
            return 1
        }

        let mutable longJob2Finished = false
        let longJob2 = async {
            do! Async.Sleep (int longTime.TotalMilliseconds)
            longJobFinished <- true
            return 2.0
        }

        let mutable longJob3Finished = false
        let longJob3 = async {
            do! Async.Sleep (int longTime.TotalMilliseconds)
            longJobFinished <- true
            return 3I
        }

        let result =
            try
                FSharpUtil.AsyncExtensions.MixedParallel4 longJob shortJob longJob2 longJob3
                |> Async.RunSynchronously |> Some
            with
            | _ -> None

        Assert.That(result, Is.EqualTo None)
        Assert.That(longJobFinished, Is.EqualTo false, "#before")
        Assert.That(longJob2Finished, Is.EqualTo false, "#before - 2")
        Assert.That(longJob3Finished, Is.EqualTo false, "#before - 3")
        Threading.Thread.Sleep(TimeSpan.FromSeconds 7.0)
        Assert.That(longJobFinished, Is.EqualTo false, "#after")
        Assert.That(longJob2Finished, Is.EqualTo false, "#after - 2")
        Assert.That(longJob3Finished, Is.EqualTo false, "#after - 3")
