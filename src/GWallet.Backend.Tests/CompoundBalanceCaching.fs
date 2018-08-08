namespace GWallet.Backend.Tests

open System
open System.IO

open NUnit.Framework

open GWallet.Backend

module CompoundBalanceCaching =

    let high_expiration_span_because_this_test_doesnt_involve_timing = TimeSpan.FromDays 100.0

    [<Test>]
    let ``combinations metatest``() =
        let someMap: Map<string,int> = Map.empty.Add("x", 1).Add("y", 2).Add("z", 3)
        let combinations = Caching.MapCombinations someMap
        Assert.That(combinations.Length, Is.EqualTo 7)

    let someAddress = "0xABC"
    let someCurrency = Currency.ETC
    let someDummyTxId = "x"

    let SpawnNewCacheInstanceToTest(expirationSpan: TimeSpan) =
        let tempFile = Path.GetTempFileName() |> FileInfo
        Caching.MainCache(Some tempFile, expirationSpan),tempFile

    [<Test>]
    let ``non-compound balance``() =
        let cache,cacheFile =
            SpawnNewCacheInstanceToTest high_expiration_span_because_this_test_doesnt_involve_timing

        try
            let someBalance = 10m
            let cachedBalance,_ = cache.RetreiveAndUpdateLastCompoundBalance someAddress someCurrency someBalance
            Assert.That(cachedBalance, Is.EqualTo someBalance)
        finally
            File.Delete cacheFile.FullName

    [<Test>]
    let ``single-compound balance``() =
        let cache,cacheFile =
            SpawnNewCacheInstanceToTest high_expiration_span_because_this_test_doesnt_involve_timing

        try
            let someBalance = 10m
            let cachedBalance,_ = cache.RetreiveAndUpdateLastCompoundBalance someAddress someCurrency someBalance
            Assert.That(cachedBalance, Is.EqualTo someBalance)

            let someTransactionValue = 1m
            cache.StoreOutgoingTransaction someAddress
                                           someCurrency
                                           someDummyTxId
                                           someTransactionValue
            match cache.RetreiveLastCompoundBalance someAddress someCurrency with
            | NotAvailable -> Assert.Fail "should have saved some balance"
            | Cached(cachedBalance2,_) ->
                Assert.That(cachedBalance2, Is.EqualTo (someBalance - someTransactionValue))
        finally
            File.Delete cacheFile.FullName

    [<Test>]
    let ``single-compound balance with dupe tx``() =
        let cache,cacheFile =
            SpawnNewCacheInstanceToTest high_expiration_span_because_this_test_doesnt_involve_timing

        try
            let someBalance = 10m
            let cachedBalance,_ = cache.RetreiveAndUpdateLastCompoundBalance someAddress someCurrency someBalance
            Assert.That(cachedBalance, Is.EqualTo someBalance)

            let someTransactionValue = 1m
            cache.StoreOutgoingTransaction someAddress
                                           someCurrency
                                           someDummyTxId
                                           someTransactionValue
            cache.StoreOutgoingTransaction someAddress
                                           someCurrency
                                           someDummyTxId
                                           someTransactionValue

            match cache.RetreiveLastCompoundBalance someAddress someCurrency with
            | NotAvailable -> Assert.Fail "should have saved some balance"
            | Cached(cachedBalance2,_) ->
                Assert.That(cachedBalance2, Is.EqualTo (someBalance - someTransactionValue))
        finally
            File.Delete cacheFile.FullName

    [<Test>]
    let ``double-compound balance``() =
        let cache,cacheFile =
            SpawnNewCacheInstanceToTest high_expiration_span_because_this_test_doesnt_involve_timing

        try
            let someBalance = 10m
            let cachedBalance,_ = cache.RetreiveAndUpdateLastCompoundBalance someAddress someCurrency someBalance
            Assert.That(cachedBalance, Is.EqualTo someBalance)

            let firstTransactionAmount = 1m
            cache.StoreOutgoingTransaction someAddress
                                           someCurrency
                                           "x"
                                           firstTransactionAmount

            let secondTransactionAmount = 2m
            cache.StoreOutgoingTransaction someAddress
                                           someCurrency
                                           "y"
                                           secondTransactionAmount

            match cache.RetreiveLastCompoundBalance someAddress someCurrency with
            | NotAvailable -> Assert.Fail "should have saved some balance"
            | Cached(cachedBalance2,_) ->
                Assert.That(cachedBalance2, Is.EqualTo (someBalance - firstTransactionAmount - secondTransactionAmount))
        finally
            File.Delete cacheFile.FullName

    [<Test>]
    let ``confirmed first transaction``() =
        let cache,cacheFile =
            SpawnNewCacheInstanceToTest high_expiration_span_because_this_test_doesnt_involve_timing

        try
            let someBalance = 10m
            let cachedBalance,_ = cache.RetreiveAndUpdateLastCompoundBalance someAddress someCurrency someBalance
            Assert.That(cachedBalance, Is.EqualTo someBalance)

            let firstTransactionAmount = 1m
            cache.StoreOutgoingTransaction someAddress
                                           someCurrency
                                           "x"
                                           firstTransactionAmount
            let secondTransactionAmount = 2m
            cache.StoreOutgoingTransaction someAddress
                                           someCurrency
                                           "y"
                                           secondTransactionAmount

            let newBalanceAfterFirstTransactionIsConfirmed = someBalance - firstTransactionAmount
            let cachedBalance2,_ = cache.RetreiveAndUpdateLastCompoundBalance someAddress
                                                                              someCurrency
                                                                              newBalanceAfterFirstTransactionIsConfirmed
            Assert.That(cachedBalance2, Is.EqualTo (someBalance - firstTransactionAmount - secondTransactionAmount))
        finally
            File.Delete cacheFile.FullName

    [<Test>]
    let ``confirmed second transaction``() =
        let cache,cacheFile =
            SpawnNewCacheInstanceToTest high_expiration_span_because_this_test_doesnt_involve_timing

        try
            let someBalance = 10m
            let cachedBalance,_ = cache.RetreiveAndUpdateLastCompoundBalance someAddress someCurrency someBalance
            Assert.That(cachedBalance, Is.EqualTo someBalance)

            let firstTransactionAmount = 1m
            cache.StoreOutgoingTransaction someAddress
                                           someCurrency
                                           "x"
                                           firstTransactionAmount
            let cachedBalance = cache.RetreiveAndUpdateLastCompoundBalance someAddress someCurrency someBalance
            let secondTransactionAmount = 2m
            cache.StoreOutgoingTransaction someAddress
                                           someCurrency
                                           "y"
                                           secondTransactionAmount

            let newBalanceAfterSndTransactionIsConfirmed = someBalance - secondTransactionAmount
            let cachedBalance2,_ = cache.RetreiveAndUpdateLastCompoundBalance someAddress
                                                                              someCurrency
                                                                              newBalanceAfterSndTransactionIsConfirmed
            Assert.That(cachedBalance2, Is.EqualTo (someBalance - firstTransactionAmount - secondTransactionAmount))
        finally
            File.Delete cacheFile.FullName

    [<Test>]
    let ``confirmed two transactions``() =
        let cache,cacheFile =
            SpawnNewCacheInstanceToTest high_expiration_span_because_this_test_doesnt_involve_timing

        try
            let someBalance = 10m
            let cachedBalance,_ = cache.RetreiveAndUpdateLastCompoundBalance someAddress someCurrency someBalance
            Assert.That(cachedBalance, Is.EqualTo someBalance)

            let firstTransactionAmount = 1m
            cache.StoreOutgoingTransaction someAddress
                                           someCurrency
                                           "x"
                                           firstTransactionAmount
            let secondTransactionAmount = 2m
            cache.StoreOutgoingTransaction someAddress
                                           someCurrency
                                           "y"
                                           secondTransactionAmount

            let newBalanceAfterBothTxsAreConfirmed = someBalance - secondTransactionAmount - firstTransactionAmount
            let cachedBalance2,_ = cache.RetreiveAndUpdateLastCompoundBalance someAddress
                                                                              someCurrency
                                                                              newBalanceAfterBothTxsAreConfirmed
            Assert.That(cachedBalance2, Is.EqualTo (someBalance - firstTransactionAmount - secondTransactionAmount))
        finally
            File.Delete cacheFile.FullName

    [<Test>]
    let ``single-compound balance with expired transaction (retrieve and update)``() =
        let expirationTime = TimeSpan.FromMilliseconds 100.0
        let cache,cacheFile =
            SpawnNewCacheInstanceToTest expirationTime

        try
            let someBalance = 10m
            let cachedBalance,_ = cache.RetreiveAndUpdateLastCompoundBalance someAddress someCurrency someBalance
            Assert.That(cachedBalance, Is.EqualTo someBalance)

            cache.StoreOutgoingTransaction someAddress
                                           someCurrency
                                           someDummyTxId
                                           1m
            Threading.Thread.Sleep(expirationTime + expirationTime)
            let cachedBalance2,_ = cache.RetreiveAndUpdateLastCompoundBalance someAddress someCurrency someBalance
            Assert.That(cachedBalance2, Is.EqualTo someBalance)
        finally
            File.Delete cacheFile.FullName

    [<Test>]
    let ``single-compound balance with expired transaction (just retreive)``() =
        let expirationTime = TimeSpan.FromMilliseconds 100.0
        let cache,cacheFile =
            SpawnNewCacheInstanceToTest expirationTime

        try
            let someBalance = 10m
            let cachedBalance,_ = cache.RetreiveAndUpdateLastCompoundBalance someAddress someCurrency someBalance
            Assert.That(cachedBalance, Is.EqualTo someBalance)

            cache.StoreOutgoingTransaction someAddress
                                           someCurrency
                                           someDummyTxId
                                           1m
            Threading.Thread.Sleep(expirationTime + expirationTime)
            match cache.RetreiveLastCompoundBalance someAddress someCurrency with
            | NotAvailable -> Assert.Fail "should have saved some balance"
            | Cached(cachedBalance2,_) ->
                Assert.That(cachedBalance2, Is.EqualTo someBalance)
        finally
            File.Delete cacheFile.FullName
