namespace GWallet.Backend.Tests

open System
open System.Linq

open NUnit.Framework

open GWallet.Backend


exception SomeSpecificException
exception SomeException
exception SomeOtherException

[<TestFixture>]
type FaultTolerance() =

    let one_consistent_result_because_this_test_doesnt_test_consistency = 1
    let not_more_than_one_parallel_job_because_this_test_doesnt_test_parallelization = 1
    let test_does_not_involve_retries = uint16 0

    let defaultSettingsForNoConsistencyNoParallelismAndNoRetries() =
        {
            NumberOfMaximumParallelJobs = uint16 1;
            ConsistencyConfig = NumberOfConsistentResponsesRequired (uint16 1);
            NumberOfRetries = uint16 0;
            NumberOfRetriesForInconsistency = uint16 0;
        }

    let defaultFaultTolerantParallelClient =
        FaultTolerantParallelClient<SomeSpecificException>()

    [<Test>]
    member __.``can retrieve basic T for single func``() =
        let someStringArg = "foo"
        let someResult = 1
        let func (arg: string) =
            someResult
        let dataRetreived =
            defaultFaultTolerantParallelClient.Query<string,int>
                (defaultSettingsForNoConsistencyNoParallelismAndNoRetries()) someStringArg [ func ]
                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo(someResult))

    [<Test>]
    member __.``can retrieve basic T for 2 funcs``() =
        let someStringArg = "foo"
        let someResult = 1
        let func1 (arg: string) =
            someResult
        let func2 (arg: string) =
            someResult
        let dataRetreived = defaultFaultTolerantParallelClient.Query<string,int>
                                (defaultSettingsForNoConsistencyNoParallelismAndNoRetries())
                                someStringArg [ func1; func2 ]
                                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo(someResult))

    [<Test>]
    member __.``throws ArgumentException if no funcs``(): unit =
        let client = defaultFaultTolerantParallelClient
        Assert.Throws<ArgumentException>(
            fun _ -> client.Query<string,int>
                            (defaultSettingsForNoConsistencyNoParallelismAndNoRetries())
                            "_" [] |> Async.RunSynchronously |> ignore
        ) |> ignore

    [<Test>]
    member __.``can retrieve one if 1 of the funcs throws``() =
        let someStringArg = "foo"
        let someResult = 1
        let func1 (arg: string) =
            raise SomeSpecificException
        let func2 (arg: string) =
            someResult
        let dataRetreived =
            defaultFaultTolerantParallelClient.Query<string,int>
                (defaultSettingsForNoConsistencyNoParallelismAndNoRetries()) someStringArg [ func1; func2 ]
                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo(someResult))

    [<Test>]
    member __.``ServerUnavailabilityException exception contains innerException``() =
        let someStringArg = "foo"
        let func1 (arg: string) =
            raise SomeException
        let func2 (arg: string) =
            raise SomeException

        let dataRetrieved =
            try
                let result =
                    (FaultTolerantParallelClient<SomeException> ())
                        .Query<string,int>
                            (defaultSettingsForNoConsistencyNoParallelismAndNoRetries()) someStringArg [ func1; func2 ]
                            |> Async.RunSynchronously
                Some(result)
            with
            | ex ->
                Assert.That (ex :? ServerUnavailabilityException, Is.True)
                Assert.That (ex.GetType().Name, Is.EqualTo "NoneAvailableException")
                Assert.That (ex.InnerException, Is.Not.Null)
                Assert.That (ex.InnerException, Is.TypeOf<SomeException>())
                None

        Assert.That(dataRetrieved, Is.EqualTo(None))

    [<Test>]
    member __.``exception typed passed in is ignored``() =
        let someResult = 1
        let someStringArg = "foo"
        let func1 (arg: string) =
            raise SomeException
        let func2 (arg: string) =
            someResult

        let result =
            (FaultTolerantParallelClient<SomeException>())
                .Query<string,int>
                    (defaultSettingsForNoConsistencyNoParallelismAndNoRetries()) someStringArg [ func1; func2 ]
                    |> Async.RunSynchronously
        Assert.That(result, Is.EqualTo(someResult))

    [<Test>]
    member __.``exception not passed in is not ignored``() =
        let someResult = 1
        let someStringArg = "foo"
        let func1 (arg: string) =
            raise SomeOtherException
        let func2 (arg: string) =
            someResult

        let ex = Assert.Throws<AggregateException>(fun _ ->
            (FaultTolerantParallelClient<SomeException>())
                .Query<string,int>
                    (defaultSettingsForNoConsistencyNoParallelismAndNoRetries()) someStringArg [ func1; func2 ]
                    |> Async.RunSynchronously
                    |> ignore )

        Assert.That((FSharpUtil.FindException<SomeOtherException> ex).IsSome, Is.True)

    [<Test>]
    member __.``exception passed in must not be SystemException, otherwise it throws``() =
        let someStringArg = "foo"
        let func1 (arg: string) =
            "someResult1"
        let func2 (arg: string) =
            "someResult2"

        Assert.Throws<ArgumentException>(fun _ ->
            (FaultTolerantParallelClient<Exception>())
                |> ignore ) |> ignore

    [<Test>]
    member __.``makes sure data is consistent across N funcs``() =
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

        let settings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                            ConsistencyConfig =
                                NumberOfConsistentResponsesRequired numberOfConsistentResponsesToBeConsideredSafe; }
        let consistencyGuardClient =
            FaultTolerantParallelClient<SomeSpecificException>()

        let dataRetreived =
            consistencyGuardClient
                .Query<string,int> settings someStringArg
                                   [ funcInconsistent; funcConsistentA; funcConsistentB; ]
                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo(someConsistentResult))

        let dataRetreived =
            consistencyGuardClient
                .Query<string,int> settings someStringArg
                                   [ funcConsistentA; funcInconsistent; funcConsistentB; ]
                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo(someConsistentResult))

        let dataRetreived =
            consistencyGuardClient
                .Query<string,int> settings someStringArg
                                   [ funcConsistentA; funcConsistentB; funcInconsistent; ]
                |> Async.RunSynchronously
        Assert.That(dataRetreived, Is.TypeOf<int>())
        Assert.That(dataRetreived, Is.EqualTo(someConsistentResult))

    [<Test>]
    member __.``consistency precondition > 0``() =
        let invalidSettings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries()
                                    with ConsistencyConfig = NumberOfConsistentResponsesRequired (uint16 0); }

        Assert.Throws<ArgumentException>(fun _ ->
            FaultTolerantParallelClient<SomeSpecificException>().Query
                invalidSettings () [ fun _ -> () ] |> Async.RunSynchronously
                    |> ignore ) |> ignore

    [<Test>]
    member __.``consistency precondition > funcs``() =
        let numberOfConsistentResponsesToBeConsideredSafe = uint16 3
        let settings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                            ConsistencyConfig =
                                NumberOfConsistentResponsesRequired numberOfConsistentResponsesToBeConsideredSafe; }

        let someStringArg = "foo"
        let someResult = 1

        let func1 (arg: string) =
            someResult
        let func2 (arg: string) =
            someResult

        let client = FaultTolerantParallelClient<SomeSpecificException>()

        Assert.Throws<ArgumentException>(fun _ ->
            client.Query<string,int>
                settings
                someStringArg
                [ func1; func2 ]
                |> Async.RunSynchronously
                    |> ignore ) |> ignore

    [<Test>]
    member __.``if consistency is not found, throws inconsistency exception``() =
        let numberOfConsistentResponsesToBeConsideredSafe = uint16 3
        let settings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                             ConsistencyConfig = NumberOfConsistentResponsesRequired numberOfConsistentResponsesToBeConsideredSafe; }

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

        let client = FaultTolerantParallelClient<SomeSpecificException>()

        let inconsistencyEx = Assert.Throws<ResultInconsistencyException>(fun _ ->
                                  client.Query<string,int>
                                      settings
                                      someStringArg
                                      [ func1; func2; func3; func4 ]
                                          |> Async.RunSynchronously
                                          |> ignore )
        Assert.That(inconsistencyEx.Message, Is.StringContaining("received: 4, consistent: 2, required: 3"))

    [<Test>]
    member __.``retries at least once if all fail``() =
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

        let settings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                            NumberOfRetries = uint16 1;
                            NumberOfRetriesForInconsistency = uint16 0; }

        let client = FaultTolerantParallelClient<SomeException>()
        client.Query<string,int> settings someStringArg [ func1; func2 ]
            |> Async.RunSynchronously
            // enough to know that it doesn't throw
            |> ignore

    [<Test>]
    member __.``it retries before throwing inconsistency exception``() =
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

        let settings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                            ConsistencyConfig =
                                NumberOfConsistentResponsesRequired numberOfConsistentResponsesToBeConsideredSafe;
                            NumberOfRetries = uint16 0;
                            NumberOfRetriesForInconsistency = uint16 1; }

        let client = FaultTolerantParallelClient<SomeSpecificException>()


        client.Query<string,int>
            settings
            someStringArg
            [ func1; func2; func3whichGetsConsistentAtSecondTry; func4 ]
                |> Async.RunSynchronously
                |> ignore

        let inconsistencyEx = Assert.Throws<ResultInconsistencyException>(fun _ ->
                                  client.Query<string,int>
                                      settings
                                      someStringArg
                                      [ func1; func2; func3whichGetsConsistentAtThirdTry; func4 ]
                                          |> Async.RunSynchronously
                                          |> ignore )
        Assert.That(inconsistencyEx.Message, Is.StringContaining("received: 4, consistent: 2, required: 3"))

    [<Test>]
    member __.``using an average func instead of consistency``() =

        let someStringArg = "foo"

        let func1 (arg: string) =
            1
        let func2 (arg: string) =
            5
        let func3 (arg: string) =
            6

        let funcs = [ func1; func2; func3; ]

        let settings = { defaultSettingsForNoConsistencyNoParallelismAndNoRetries() with
                            NumberOfMaximumParallelJobs = uint16 funcs.Length
                            ConsistencyConfig =
                                AverageBetweenResponses (uint16 funcs.Length,
                                                         (fun (list:List<int>) ->
                                                             list.Sum() / list.Length
                                                         )); }

        let client = FaultTolerantParallelClient<SomeSpecificException>()

        let result = client.Query<string,int>
                         settings
                         someStringArg
                         funcs
                             |> Async.RunSynchronously

        Assert.That(result, Is.EqualTo ((1+5+6)/3))

    member private __.DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() =
        defaultSettingsForNoConsistencyNoParallelismAndNoRetries()

    static member DefaultSettingsForNoConsistencyNoParallelismAndNoRetries() =
        FaultTolerance().DefaultSettingsForNoConsistencyNoParallelismAndNoRetries()
