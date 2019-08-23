namespace GWallet.Backend.Tests

open System

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

        let res1 =
            FSharpUtil.AsyncExtensions.WhenAny [longJob; shortJob]
            |> Async.RunSynchronously
        Assert.That(res1, Is.EqualTo shortJobRes)

        let res2 =
            FSharpUtil.AsyncExtensions.WhenAny [shortJob; longJob]
            |> Async.RunSynchronously
        Assert.That(res2, Is.EqualTo shortJobRes)

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

        let res1 =
            Async.Choice [longJob; shortFailingJob; shortSuccessfulJob]
            |> Async.RunSynchronously
        Assert.That(res1, Is.EqualTo (Some shortSuccessfulJobRes))

        let res2 =
            Async.Choice [longJob; shortSuccessfulJob; shortFailingJob]
            |> Async.RunSynchronously
        Assert.That(res2, Is.EqualTo (Some shortSuccessfulJobRes))

        let res3 =
            Async.Choice [shortFailingJob; longJob; shortSuccessfulJob]
            |> Async.RunSynchronously
        Assert.That(res3, Is.EqualTo (Some shortSuccessfulJobRes))

        let res4 =
            Async.Choice [shortFailingJob; shortSuccessfulJob; longJob]
            |> Async.RunSynchronously
        Assert.That(res4, Is.EqualTo (Some shortSuccessfulJobRes))

        let res5 =
            Async.Choice [shortSuccessfulJob; longJob; shortFailingJob]
            |> Async.RunSynchronously
        Assert.That(res5, Is.EqualTo (Some shortSuccessfulJobRes))

        let res6 =
            Async.Choice [shortSuccessfulJob; shortFailingJob; longJob]
            |> Async.RunSynchronously
        Assert.That(res6, Is.EqualTo (Some shortSuccessfulJobRes))



