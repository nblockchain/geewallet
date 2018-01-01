namespace GWallet.Backend.Tests

open System
open System.Linq

open NUnit.Framework

open GWallet.Backend

module Shuffling =

    [<Test>]
    let ``retreives same number of elements``() =
        let someList = [ 1; 2; 3; 4; 5; 6; 7; 8; 9; 10; ]
        let randomizedList = Shuffler.Unsort someList

        Assert.That(randomizedList.Count(), Is.EqualTo(10))

    [<Test>]
    let ``doesn't return same list'``() =
        let someList = [ 1; 2; 3; 4; 5; 6; 7; 8; 9; 10; ]
        let randomizedList = Shuffler.Unsort someList

        // very unlikely to give a false positive:
        Assert.That(someList, Is.Not.EqualTo(randomizedList))

    [<Test>]
    let ``doesn't randomize in the same way'``() =
        let someList = [ 1; 2; 3; 4; 5; 6; 7; 8; 9; 10; ]
        let randomizedList1 = Shuffler.Unsort someList
        let randomizedList2 = Shuffler.Unsort someList

        // very unlikely to give a false positive:
        Assert.That(randomizedList1, Is.Not.EqualTo(randomizedList2))
