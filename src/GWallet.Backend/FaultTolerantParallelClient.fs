namespace GWallet.Backend

open System
open System.Linq
open System.Diagnostics
open System.Threading
open System.Threading.Tasks

type ResourceUnavailabilityException (message: string, innerOrLastException: Exception) =
    inherit Exception (message, innerOrLastException)

type private TaskUnavailabilityException (message: string, innerException: Exception) =
    inherit ResourceUnavailabilityException (message, innerException)

type private ServerUnavailabilityException (message: string, lastException: Exception) =
    inherit ResourceUnavailabilityException (message, lastException)

type private NoneAvailableException (message:string, lastException: Exception) =
   inherit ServerUnavailabilityException (message, lastException)

type private NotEnoughAvailableException (message:string, lastException: Exception) =
   inherit ServerUnavailabilityException (message, lastException)

type ResultInconsistencyException (totalNumberOfSuccesfulResultsObtained: int,
                                   maxNumberOfConsistentResultsObtained: int,
                                   numberOfConsistentResultsRequired: uint32) =
  inherit Exception ("Results obtained were not enough to be considered consistent" +
                      sprintf " (received: %d, consistent: %d, required: %d)"
                                  totalNumberOfSuccesfulResultsObtained
                                  maxNumberOfConsistentResultsObtained
                                  numberOfConsistentResultsRequired)

type internal ResultsSoFar<'R> = List<'R>
type internal ExceptionsSoFar<'K,'R,'E when 'K: equality and 'E :> Exception> = List<Server<'K,'R>*'E>
type internal FinalResult<'K,'T,'R,'E when 'K: equality and 'E :> Exception> =
    | ConsistentResult of 'R
    | AverageResult of 'R
    | InconsistentOrNotEnoughResults of ResultsSoFar<'R>*ExceptionsSoFar<'K,'R,'E>

type internal NonParallelResultWithAdditionalWork<'K,'R,'E when 'K: equality and 'E :> Exception> =
    | SuccessfulFirstResult of ('R * Async<NonParallelResults<'K,'R,'E>>)
    | NoneAvailable
and internal NonParallelResults<'K,'R,'E when 'K: equality and 'E :> Exception> =
    ExceptionsSoFar<'K,'R,'E> * NonParallelResultWithAdditionalWork<'K,'R,'E>

type ConsistencySettings<'R> =
    | NumberOfConsistentResponsesRequired of uint32
    | AverageBetweenResponses of (uint32 * (List<'R> -> 'R))

type Mode =
    | Fast
    | Analysis

type FaultTolerantParallelClientSettings<'R> =
    {
        NumberOfMaximumParallelJobs: uint32;
        ConsistencyConfig: ConsistencySettings<'R>;
        NumberOfRetries: uint32;
        NumberOfRetriesForInconsistency: uint32;
        Mode: Mode
    }


type FaultTolerantParallelClient<'K,'E when 'K: equality and 'E :> Exception>(updateServer: 'K*HistoryInfo -> unit) =
    do
        if typeof<'E> = typeof<Exception> then
            raise (ArgumentException("'E cannot be System.Exception, use a derived one", "'E"))

    let MeasureConsistency (results: List<'R>) =
        results |> Seq.countBy id |> Seq.sortByDescending (fun (_,count: int) -> count) |> List.ofSeq

    let LaunchAsyncJobs (jobs:List<Async<NonParallelResults<'K,'R,'E>>>)
                        (cancellationSource: CancellationTokenSource)
                            : List<Task<NonParallelResults<'K,'R,'E>>> =
        let token =
            try
                cancellationSource.Token
            with
            | :? ObjectDisposedException as ex ->
                raise <| TaskUnavailabilityException("cancellationTokenSource already disposed", ex)

        jobs
            |> List.map (fun job -> Async.StartAsTask(job, ?cancellationToken = Some token))

    let rec WhenSomeInternal (consistencySettings: ConsistencySettings<'R>)
                             (tasks: List<Task<NonParallelResults<'K,'R,'E>>>)
                             (resultsSoFar: List<'R>)
                             (failedFuncsSoFar: ExceptionsSoFar<'K,'R,'E>)
                             : Async<FinalResult<'K,'T,'R,'E>> = async {
        match tasks with
        | [] ->
            return InconsistentOrNotEnoughResults(resultsSoFar,failedFuncsSoFar)
        | theTasks ->

            let taskToWaitForFirstFinishedTask = Task.WhenAny theTasks
            let! fastestTask = Async.AwaitTask taskToWaitForFirstFinishedTask
            let failuresOfTask,resultOfTask = fastestTask.Result

            let restOfTasks: List<Task<NonParallelResults<'K,'R,'E>>> =
                theTasks.Where(fun task -> not (Object.ReferenceEquals(task, fastestTask))) |> List.ofSeq

            let (newResults,newRestOfTasks) =
                match resultOfTask with
                | SuccessfulFirstResult(newResult,unlaunchedJobWithMoreTasks) ->
                    let newTask = Async.StartAsTask unlaunchedJobWithMoreTasks
                    (newResult::resultsSoFar),(newTask::restOfTasks)
                | NoneAvailable ->
                    resultsSoFar,restOfTasks
            let newFailedFuncs = List.append failedFuncsSoFar failuresOfTask

            match consistencySettings with
            | AverageBetweenResponses (minimumNumberOfResponses,averageFunc) ->
                if (newResults.Length >= int minimumNumberOfResponses) then
                    return AverageResult (averageFunc newResults)
                else
                    return! WhenSomeInternal consistencySettings newRestOfTasks newResults newFailedFuncs
            | NumberOfConsistentResponsesRequired number ->
                let resultsSortedByCount = MeasureConsistency newResults
                match resultsSortedByCount with
                | [] ->
                    return! WhenSomeInternal consistencySettings newRestOfTasks newResults newFailedFuncs
                | (mostConsistentResult,maxNumberOfConsistentResultsObtained)::_ ->
                    if (maxNumberOfConsistentResultsObtained = int number) then
                        return ConsistentResult mostConsistentResult
                    else
                        return! WhenSomeInternal consistencySettings newRestOfTasks newResults newFailedFuncs
    }

    // at the time of writing this, I only found a Task.WhenAny() equivalent function in the asyncF# world, called
    // "Async.WhenAny" in TomasP's tryJoinads source code, however it seemed a bit complex for me to wrap my head around
    // it (and I couldn't just consume it and call it a day, I had to modify it to be "WhenSome" instead of "WhenAny",
    // as in when N>1), so I decided to write my own, using Tasks to make sure I would not spawn duplicate jobs
    let WhenSome (consistencySettings: ConsistencySettings<'R>)
                 (jobs: List<Async<NonParallelResults<'K,'R,'E>>>)
                 (resultsSoFar: List<'R>)
                 (failedFuncsSoFar: ExceptionsSoFar<'K,'R,'E>)
                 (cancellationSource: CancellationTokenSource)
                 : Async<FinalResult<'K,'T,'R,'E>> =
        let tasks = LaunchAsyncJobs jobs cancellationSource
        WhenSomeInternal consistencySettings tasks resultsSoFar failedFuncsSoFar

    let rec ConcatenateNonParallelFuncs (failuresSoFar: ExceptionsSoFar<'K,'R,'E>)
                                        (servers: List<Server<'K,'R>>)
                                        : Async<NonParallelResults<'K,'R,'E>> =
        match servers with
        | [] ->
            async {
                return failuresSoFar,NoneAvailable
            }
        | head::tail ->
            async {
                let stopwatch = Stopwatch()
                stopwatch.Start()
                try
                    let! result = head.Retrieval
                    stopwatch.Stop()
                    updateServer (head.Identifier, { Fault = None; TimeSpan = stopwatch.Elapsed })
                    let tailAsync = ConcatenateNonParallelFuncs failuresSoFar tail
                    return failuresSoFar,SuccessfulFirstResult(result,tailAsync)
                with
                | ex ->
                    stopwatch.Stop()
                    let maybeSpecificEx = FSharpUtil.FindException<'E> ex
                    match maybeSpecificEx with
                    | Some specificInnerEx ->
                        if (Config.DebugLog) then
                            Console.Error.WriteLine (sprintf "Fault warning: %s: %s"
                                                         (ex.GetType().FullName)
                                                         ex.Message)
                        let exInfo =
                            {
                                TypeFullName = specificInnerEx.GetType().FullName
                                Message = specificInnerEx.Message
                            }
                        updateServer (head.Identifier, { Fault = Some exInfo; TimeSpan = stopwatch.Elapsed })
                        let newFailures = (head,specificInnerEx)::failuresSoFar
                        return! ConcatenateNonParallelFuncs newFailures tail
                    | None ->
                        return raise (FSharpUtil.ReRaise ex)
            }

    let CancelAndDispose (source: CancellationTokenSource) =
        try
            source.Cancel()
            source.Dispose()
        with
        | :? ObjectDisposedException ->
            ()

    let rec QueryInternalImplementation
                          (settings: FaultTolerantParallelClientSettings<'R>)
                          (funcs: List<Server<'K,'R>>)
                          (resultsSoFar: List<'R>)
                          (failedFuncsSoFar: ExceptionsSoFar<'K,'R,'E>)
                          (retries: uint32)
                          (retriesForInconsistency: uint32)
                          (cancellationSource: CancellationTokenSource)
                              : Async<'R> = async {
        if not (funcs.Any()) then
            return raise(ArgumentException("number of funcs must be higher than zero",
                                           "funcs"))
        let howManyFuncs = uint32 funcs.Length
        let numberOfMaximumParallelJobs = int settings.NumberOfMaximumParallelJobs

        match settings.ConsistencyConfig with
        | NumberOfConsistentResponsesRequired numberOfConsistentResponsesRequired ->
            if numberOfConsistentResponsesRequired < 1u then
                return raise <| ArgumentException("must be higher than zero", "numberOfConsistentResponsesRequired")
            if (howManyFuncs < numberOfConsistentResponsesRequired) then
                return raise(ArgumentException("number of funcs must be equal or higher than numberOfConsistentResponsesRequired",
                                               "funcs"))
        | AverageBetweenResponses(minimumNumberOfResponses,averageFunc) ->
            if (int minimumNumberOfResponses > numberOfMaximumParallelJobs) then
                return raise(ArgumentException("numberOfMaximumParallelJobs should be equal or higher than minimumNumberOfResponses for the averageFunc",
                                               "settings"))

        let funcsToRunInParallel,restOfFuncs =
            if (howManyFuncs > settings.NumberOfMaximumParallelJobs) then
                funcs |> Seq.take numberOfMaximumParallelJobs, funcs |> Seq.skip numberOfMaximumParallelJobs
            else
                funcs |> Seq.ofList, Seq.empty

        // each bucket can be run in parallel, each bucket contains 1 or more funcs that cannot be run in parallel
        // e.g. if we have funcs A, B, C, D and numberOfMaximumParallelJobs=2, then we have funcBucket1(A,B) and
        //      funcBucket2(C,D), then fb1&fb2 are started at the same time (A&C start at the same time), and B
        //      starts only when A finishes or fails, and D only starts when C finishes or fails
        let funcBuckets =
            Seq.splitInto numberOfMaximumParallelJobs funcs
            |> Seq.map List.ofArray
            |> Seq.map (ConcatenateNonParallelFuncs List.empty)
            |> List.ofSeq

        let lengthOfBucketsSanityCheck = Math.Min(funcs.Length, numberOfMaximumParallelJobs)
        if (lengthOfBucketsSanityCheck <> funcBuckets.Length) then
            return failwithf "Assertion failed, splitInto didn't work as expected? got %d, should be %d"
                             funcBuckets.Length lengthOfBucketsSanityCheck

        let! result =
            WhenSome settings.ConsistencyConfig funcBuckets resultsSoFar failedFuncsSoFar cancellationSource
        match result with
        | AverageResult averageResult ->
            CancelAndDispose cancellationSource
            return averageResult
        | ConsistentResult consistentResult ->
            CancelAndDispose cancellationSource
            return consistentResult
        | InconsistentOrNotEnoughResults(allResultsSoFar,failedFuncsWithTheirExceptions) ->
            let failedFuncs = failedFuncsWithTheirExceptions |> List.map fst
            if (allResultsSoFar.Length = 0) then
                if (retries = settings.NumberOfRetries) then
                    let firstEx = failedFuncsWithTheirExceptions.First() |> snd
                    CancelAndDispose cancellationSource
                    return raise (NoneAvailableException("Not available", firstEx))
                else
                    return! QueryInternalImplementation
                                          settings
                                          failedFuncs
                                          allResultsSoFar
                                          List.Empty
                                          (retries + 1u)
                                          retriesForInconsistency
                                          cancellationSource
            else
                let totalNumberOfSuccesfulResultsObtained = allResultsSoFar.Length
                match settings.ConsistencyConfig with
                | NumberOfConsistentResponsesRequired numberOfConsistentResponsesRequired ->
                    let resultsOrderedByCount = MeasureConsistency allResultsSoFar
                    match resultsOrderedByCount with
                    | [] ->
                        return failwith "resultsSoFar.Length != 0 but MeasureConsistency returns None, please report this bug"
                    | (mostConsistentResult,maxNumberOfConsistentResultsObtained)::_ ->
                        if (retriesForInconsistency = settings.NumberOfRetriesForInconsistency) then
                            CancelAndDispose cancellationSource
                            return raise (ResultInconsistencyException(totalNumberOfSuccesfulResultsObtained,
                                                                       maxNumberOfConsistentResultsObtained,
                                                                       numberOfConsistentResponsesRequired))
                        else
                            return! QueryInternalImplementation
                                                  settings
                                                  funcs
                                                  List.Empty
                                                  List.Empty
                                                  retries
                                                  (retriesForInconsistency + 1u)
                                                  cancellationSource
                | AverageBetweenResponses(minimumNumberOfResponses,averageFunc) ->
                    if (retries = settings.NumberOfRetries) then
                        let firstEx = failedFuncsWithTheirExceptions.First() |> snd
                        CancelAndDispose cancellationSource
                        return raise (NotEnoughAvailableException("resultsSoFar.Length != 0 but not enough to satisfy minimum number of results for averaging func", firstEx))
                    else
                        return! QueryInternalImplementation
                                              settings
                                              failedFuncs
                                              allResultsSoFar
                                              failedFuncsWithTheirExceptions
                                              (retries + 1u)
                                              retriesForInconsistency
                                              cancellationSource

    }

    let OrderServers (servers: List<Server<'K,'R>>) (mode: Mode): List<Server<'K,'R>> =
        let workingServers = List.filter (fun server ->
                                             match server.HistoryInfo with
                                             | None ->
                                                 false
                                             | Some historyInfo ->
                                                 match historyInfo.Fault with
                                                 | None ->
                                                     true
                                                 | Some _ ->
                                                     false
                                         ) servers
        let sortedWorkingServers =
            List.sortBy
                (fun server ->
                    match server.HistoryInfo with
                    | None ->
                        failwith "previous filter didn't work? should get working servers only, not lacking history"
                    | Some historyInfo ->
                        match historyInfo.Fault with
                        | None ->
                            historyInfo.TimeSpan
                        | Some _ ->
                            failwith "previous filter didn't work? should get working servers only, not faulty"
                )
                workingServers

        let serversWithNoHistoryServers = List.filter (fun server -> server.HistoryInfo.IsNone) servers

        let faultyServers = List.filter (fun server ->
                                            match server.HistoryInfo with
                                            | None ->
                                                false
                                            | Some historyInfo ->
                                                match historyInfo.Fault with
                                                | None ->
                                                    false
                                                | Some _ ->
                                                    true
                                        ) servers
        let sortedFaultyServers =
            List.sortBy
                (fun server ->
                    match server.HistoryInfo with
                    | None ->
                        failwith "previous filter didn't work? should get working servers only, not lacking history"
                    | Some historyInfo ->
                        match historyInfo.Fault with
                        | None ->
                            failwith "previous filter didn't work? should get faulty servers only, not working ones"
                        | Some _ ->
                            historyInfo.TimeSpan
                )
                faultyServers

        if mode = Mode.Fast then
            List.append sortedWorkingServers (List.append serversWithNoHistoryServers sortedFaultyServers)
        else
            let intersectionOffset = 3u
            let result = FSharpUtil.ListIntersect
                                     (List.append serversWithNoHistoryServers sortedWorkingServers)
                                     sortedFaultyServers
                                     intersectionOffset
            let randomizationOffset = intersectionOffset + 1u
            Shuffler.RandomizeEveryNthElement result randomizationOffset

    member private self.QueryInternal<'R when 'R : equality>
                            (settings: FaultTolerantParallelClientSettings<'R>)
                            (servers: List<Server<'K,'R>>)
                            (cancellationTokenSourceOption: Option<CancellationTokenSource>)
                                : Async<'R> =
        if settings.NumberOfMaximumParallelJobs < 1u then
            raise (ArgumentException("must be higher than zero", "numberOfMaximumParallelJobs"))

        let effectiveCancellationSource =
            match cancellationTokenSourceOption with
            | None ->
                new CancellationTokenSource()
            | Some cancellationSource ->
                cancellationSource

        QueryInternalImplementation
            settings
            (OrderServers servers settings.Mode)
            List.Empty
            List.Empty
            0u
            0u
            effectiveCancellationSource

    member self.QueryWithCancellation<'R when 'R : equality>
                    (cancellationTokenSource: CancellationTokenSource)
                    (settings: FaultTolerantParallelClientSettings<'R>)
                    (servers: List<Server<'K,'R>>)
                        : Async<'R> =
        self.QueryInternal<'R> settings servers (Some cancellationTokenSource)

    member self.Query<'R when 'R : equality> (settings: FaultTolerantParallelClientSettings<'R>)
                                             (servers: List<Server<'K,'R>>)
                                                 : Async<'R> =
        self.QueryInternal<'R> settings servers None
