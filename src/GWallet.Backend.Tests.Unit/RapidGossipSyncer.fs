namespace GWallet.Backend.Tests.Unit

open System

open NUnit.Framework

open GWallet.Backend
open GWallet.Backend.UtxoCoin.Lightning


[<TestFixture>]
type RapidGossipSyncer() =

    static let syncData =
        lazy(
            use httpClient = new Net.Http.HttpClient()
            let baseUrl = "https://github.com/nblockchain/geewallet-binary-dependencies/raw/master/tests/RGS/"
            let full = httpClient.GetByteArrayAsync(baseUrl + "rgs_full") |> Async.AwaitTask
            let incremental = httpClient.GetByteArrayAsync(baseUrl + "rgs_incr_1663545600") |> Async.AwaitTask
            FSharpUtil.AsyncExtensions.MixedParallel2 full incremental
            |> Async.RunSynchronously
        )

    [<Test>]
    member __.Deserialization() =
        // Regression test for sync data deserialization and graph updating
        let fullData, incrementalData = syncData.Force()

        let timestampBeforeSync = RapidGossipSyncer.GetLastSyncTimestamp()
        let edgeCountBeforeSync = RapidGossipSyncer.GetGraphEdgeCount()
        Assert.AreEqual(timestampBeforeSync, 0u)
        Assert.AreEqual(edgeCountBeforeSync, 0)

        RapidGossipSyncer.SyncUsingData fullData |> Async.RunSynchronously
        let timestampAfterFullSync = RapidGossipSyncer.GetLastSyncTimestamp()
        let edgeCountAfterFullSync = RapidGossipSyncer.GetGraphEdgeCount()
        Assert.AreEqual(timestampAfterFullSync, 1663545600u)
        Assert.AreEqual(edgeCountAfterFullSync, 175343)

        RapidGossipSyncer.SyncUsingData incrementalData |> Async.RunSynchronously
        let timestampAfterIncrementalSync = RapidGossipSyncer.GetLastSyncTimestamp()
        let edgeCountAfterIncrementalSync = RapidGossipSyncer.GetGraphEdgeCount()
        Assert.AreEqual(timestampAfterIncrementalSync, 1663632000u)
        Assert.AreEqual(edgeCountAfterIncrementalSync, 176044)
