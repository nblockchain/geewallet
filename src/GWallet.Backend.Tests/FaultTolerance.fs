namespace GWallet.Backend.Tests

open System

open NUnit.Framework

open GWallet.Backend

module FaultTolerance =

    exception SomeSpecificException

    let private one_consistent_result_because_this_test_doesnt_test_consistency = 1
    let private not_more_than_one_parallel_job_because_this_test_doesnt_test_parallelization = 1
    let internal test_does_not_involve_retries = uint16 0

    let defaultSettingsForNoConsistencyNoParallelismAndNoRetries =
        {
            NumberOfMaximumParallelJobs = uint16 1;
            NumberOfConsistentResponsesRequired = uint16 1;
            NumberOfRetries = uint16 0;
            NumberOfRetriesForInconsistency = uint16 0;
        }

    let private defaultFaultTolerantParallelClient =
        FaultTolerantParallelClient<SomeSpecificException>(defaultSettingsForNoConsistencyNoParallelismAndNoRetries)

    [<Test>]
    let ``can retrieve basic T for single func``() =
        let someStringArg = "foo"
        let someResult = 1
        let func (arg: string) =
            someResult
        let dataRetreived =
            defaultFaultTolerantParallelClient.Query<string,int> someStringArg [ func ]
                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo(someResult))

    [<Test>]
    let ``can retrieve basic T for 2 funcs``() =
        let someStringArg = "foo"
        let someResult = 1
        let func1 (arg: string) =
            someResult
        let func2 (arg: string) =
            someResult
        let dataRetreived = defaultFaultTolerantParallelClient.Query<string,int> someStringArg [ func1; func2 ]
                                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo(someResult))

    [<Test>]
    let ``throws ArgumentException if no funcs``(): unit =
        let client = defaultFaultTolerantParallelClient
        Assert.Throws<ArgumentException>(
            fun _ -> client.Query<string,int> "_" [] |> Async.RunSynchronously |> ignore
        ) |> ignore

    [<Test>]
    let ``can retrieve one if 1 of the funcs throws``() =
        let someStringArg = "foo"
        let someResult = 1
        let func1 (arg: string) =
            raise SomeSpecificException
        let func2 (arg: string) =
            someResult
        let dataRetreived =
            defaultFaultTolerantParallelClient.Query<string,int> someStringArg [ func1; func2 ]
                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo(someResult))

    exception SomeException
    [<Test>]
    let ``NoneAvailable exception contains innerException``() =
        let someStringArg = "foo"
        let func1 (arg: string) =
            raise SomeException
        let func2 (arg: string) =
            raise SomeException

        let dataRetrieved =
            try
                let result =
                    (FaultTolerantParallelClient<SomeException> (defaultSettingsForNoConsistencyNoParallelismAndNoRetries))
                        .Query<string,int> someStringArg [ func1; func2 ]
                            |> Async.RunSynchronously
                Some(result)
            with
            | ex ->
                Assert.That (ex, Is.TypeOf<NoneAvailableException>(), "ex.Message is: " + ex.Message)
                Assert.That (ex.InnerException, Is.Not.Null)
                Assert.That (ex.InnerException, Is.TypeOf<SomeException>())
                None

        Assert.That(dataRetrieved, Is.EqualTo(None))

    [<Test>]
    let ``exception typed passed in is ignored``() =
        let someResult = 1
        let someStringArg = "foo"
        let func1 (arg: string) =
            raise SomeException
        let func2 (arg: string) =
            someResult

        let result =
            (FaultTolerantParallelClient<SomeException>(defaultSettingsForNoConsistencyNoParallelismAndNoRetries))
                .Query<string,int> someStringArg [ func1; func2 ]
                    |> Async.RunSynchronously
        Assert.That(result, Is.EqualTo(someResult))

    exception SomeOtherException
    [<Test>]
    let ``exception not passed in is not ignored``() =
        let someResult = 1
        let someStringArg = "foo"
        let func1 (arg: string) =
            raise SomeOtherException
        let func2 (arg: string) =
            someResult

        let ex = Assert.Throws<AggregateException>(fun _ ->
            (FaultTolerantParallelClient<SomeException>(defaultSettingsForNoConsistencyNoParallelismAndNoRetries))
                .Query<string,int> someStringArg [ func1; func2 ]
                    |> Async.RunSynchronously
                    |> ignore )

        Assert.That((FSharpUtil.FindException<SomeOtherException> ex).IsSome, Is.True)

    [<Test>]
    let ``exception passed in must not be SystemException, otherwise it throws``() =
        let someStringArg = "foo"
        let func1 (arg: string) =
            "someResult1"
        let func2 (arg: string) =
            "someResult2"

        Assert.Throws<ArgumentException>(fun _ ->
            (FaultTolerantParallelClient<Exception>(defaultSettingsForNoConsistencyNoParallelismAndNoRetries))
                |> ignore ) |> ignore

    [<Test>]
    let ``makes sure data is consistent across N funcs``() =
        let numberOfConsistentResponsesToBeConsideredSafe = uint16 2

        let someStringArg = "foo"
        let someConsistentResult = 1
        let someInconsistentResult = 2

        let funcInconsistent (arg: string) =
            someInconsistentResult
        let funcConsistentA (arg: string) =
            someConsistentResult
        let funcConsistentB (arg: string) =
            someConsistentResult

        let settings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries
                         with NumberOfConsistentResponsesRequired = numberOfConsistentResponsesToBeConsideredSafe; }
        let consistencyGuardClient =
            FaultTolerantParallelClient<SomeSpecificException>(settings)

        let dataRetreived =
            consistencyGuardClient
                .Query<string,int> someStringArg
                                   [ funcInconsistent; funcConsistentA; funcConsistentB; ]
                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo(someConsistentResult))

        let dataRetreived =
            consistencyGuardClient
                .Query<string,int> someStringArg
                                   [ funcConsistentA; funcInconsistent; funcConsistentB; ]
                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo(someConsistentResult))

        let dataRetreived =
            consistencyGuardClient
                .Query<string,int> someStringArg
                                   [ funcConsistentA; funcConsistentB; funcInconsistent; ]
                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo(someConsistentResult))

    [<Test>]
    let ``consistency precondition > 0``() =
        let invalidSettings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries
                                 with NumberOfConsistentResponsesRequired = uint16 0; }

        Assert.Throws<ArgumentException>(fun _ ->
            FaultTolerantParallelClient<SomeSpecificException>(invalidSettings)
                    |> ignore ) |> ignore

    [<Test>]
    let ``consistency precondition > funcs``() =
        let numberOfConsistentResponsesToBeConsideredSafe = uint16 3
        let settings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries
                         with NumberOfConsistentResponsesRequired = numberOfConsistentResponsesToBeConsideredSafe; }

        let someStringArg = "foo"
        let someResult = 1

        let func1 (arg: string) =
            someResult
        let func2 (arg: string) =
            someResult

        let client = FaultTolerantParallelClient<SomeSpecificException>(settings)

        Assert.Throws<ArgumentException>(fun _ ->
            client.Query<string,int>
                someStringArg
                [ func1; func2 ]
                |> Async.RunSynchronously
                    |> ignore ) |> ignore

    [<Test>]
    let ``if consistency is not found, throws inconsistency exception``() =
        let numberOfConsistentResponsesToBeConsideredSafe = uint16 3
        let settings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries
                         with NumberOfConsistentResponsesRequired = numberOfConsistentResponsesToBeConsideredSafe; }

        let someStringArg = "foo"
        let mostConsistentResult = 1
        let someOtherResultA = 2
        let someOtherResultB = 3

        let func1 (arg: string) =
            someOtherResultA
        let func2 (arg: string) =
            mostConsistentResult
        let func3 (arg: string) =
            someOtherResultB
        let func4 (arg: string) =
            mostConsistentResult

        let client = FaultTolerantParallelClient<SomeSpecificException>(settings)

        let inconsistencyEx = Assert.Throws<ResultInconsistencyException>(fun _ ->
                                  client.Query<string,int>
                                      someStringArg
                                      [ func1; func2; func3; func4 ]
                                          |> Async.RunSynchronously
                                          |> ignore )
        Assert.That(inconsistencyEx.Message, Is.StringContaining("received: 4, consistent: 2, required: 3"))

    [<Test>]
    let ``retries at least once if all fail``() =
        let someStringArg = "foo"
        let mutable count1 = 0
        let func1 (arg: string) =
            count1 <- count1 + 1
            if (count1 = 1) then
                raise SomeException
            0
        let mutable count2 = 0
        let func2 (arg: string) =
            count2 <- count2 + 1
            if (count2 = 1) then
                raise SomeException
            0

        let settings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries with
                            NumberOfRetries = uint16 1;
                            NumberOfRetriesForInconsistency = uint16 0; }

        let client = FaultTolerantParallelClient<SomeException>(settings)
        client.Query<string,int> someStringArg [ func1; func2 ]
            |> Async.RunSynchronously
            // enough to know that it doesn't throw
            |> ignore

    [<Test>]
    let ``it retries before throwing inconsistency exception``() =
        let numberOfConsistentResponsesToBeConsideredSafe = uint16 3

        let someStringArg = "foo"
        let mostConsistentResult = 1
        let someOtherResultA = 2
        let someOtherResultB = 3


        let func1 (arg: string) =
            someOtherResultA
        let func2 (arg: string) =
            mostConsistentResult

        let mutable countA = 0
        let func3whichGetsConsistentAtSecondTry (arg: string) =
            countA <- countA + 1
            if (countA = 1) then
                someOtherResultB
            else
                mostConsistentResult

        let mutable countB = 0
        let func3whichGetsConsistentAtThirdTry (arg: string) =
            countB <- countB + 1
            if (countB < 3) then
                someOtherResultB
            else
                mostConsistentResult
        let func4 (arg: string) =
            mostConsistentResult

        let settings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries with
                            NumberOfConsistentResponsesRequired = numberOfConsistentResponsesToBeConsideredSafe
                            NumberOfRetries = uint16 0;
                            NumberOfRetriesForInconsistency = uint16 1; }

        let client = FaultTolerantParallelClient<SomeSpecificException>(settings)


        client.Query<string,int>
            someStringArg
            [ func1; func2; func3whichGetsConsistentAtSecondTry; func4 ]
                |> Async.RunSynchronously
                |> ignore

        let inconsistencyEx = Assert.Throws<ResultInconsistencyException>(fun _ ->
                                  client.Query<string,int>
                                      someStringArg
                                      [ func1; func2; func3whichGetsConsistentAtThirdTry; func4 ]
                                          |> Async.RunSynchronously
                                          |> ignore )
        Assert.That(inconsistencyEx.Message, Is.StringContaining("received: 4, consistent: 2, required: 3"))
