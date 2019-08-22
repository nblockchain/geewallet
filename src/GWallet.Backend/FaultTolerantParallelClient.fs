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

type UnsuccessfulServer<'K,'R,'E when 'K: equality and 'K :> ICommunicationHistory and 'E :> Exception> =
    {
        Server: Server<'K,'R>
        Failure: 'E
    }
type ExecutedServers<'K,'R,'E when 'K: equality and 'K :> ICommunicationHistory and 'E :> Exception> =
    {
        SuccessfulResults: List<'R>
        UnsuccessfulServers: List<UnsuccessfulServer<'K,'R,'E>>
    }
type internal FinalResult<'K,'T,'R,'E when 'K: equality and 'K :> ICommunicationHistory and 'E :> Exception> =
    | ConsistentResult of 'R
    | AverageResult of 'R
    | InconsistentOrNotEnoughResults of ExecutedServers<'K,'R,'E>

type internal NonParallelResults<'K,'R,'E when 'K: equality and 'K :> ICommunicationHistory and 'E :> Exception> =
    {
        PossibleResult: Option<'R>
        Failures: List<UnsuccessfulServer<'K,'R,'E>>
        PendingWork: Option<Async<NonParallelResults<'K,'R,'E>>>
    }

type ConsistencySettings<'R> =

    // fun passed represents if cached value matches or not
    | OneServerConsistentWithCacheOrTwoServers of ('R->bool)

    | SpecificNumberOfConsistentResponsesRequired of uint32
    | AverageBetweenResponses of (uint32 * (List<'R> -> 'R))

type ServerSelectionMode =
    | Fast
    | Analysis

type ResultSelectionSettings<'R> =
    {
        ServerSelectionMode: ServerSelectionMode
        ReportUncancelledJobs: bool
        ConsistencyConfig: ConsistencySettings<'R>
    }

type ResultSelectionMode<'R> =
    | Selective of ResultSelectionSettings<'R>
    | Exhaustive

type FaultTolerantParallelClientSettings<'R> =
    {
        NumberOfParallelJobsAllowed: uint32;
        NumberOfRetries: uint32;
        NumberOfRetriesForInconsistency: uint32;
        ResultSelectionMode: ResultSelectionMode<'R>
    }

type Result<'Value, 'Err> =
    | Error of 'Err
    | Success of 'Value

type MutableStateUnsafeAccessor<'T>(initialState: 'T) =
    let mutable state = initialState
    member this.Value
        with get() =
            state
         and set value =
            state <- value

type MutableStateCapsule<'T>(initialState: 'T) =
    let state = MutableStateUnsafeAccessor initialState
    let lockObject = Object()
    member this.SafeDo (func: MutableStateUnsafeAccessor<'T>->'R): 'R =
        lock lockObject (fun _ -> func state)

type Runner<'Resource,'Ex when 'Resource: equality and 'Ex :> Exception> =
    static member Run (server: Server<_,'Resource>)
                      (stopwatch: Stopwatch)
                      (internallyCancelled: MutableStateCapsule<Option<DateTime>>)
                      (shouldReportUncancelledJobs: bool)
                          : Async<Result<'Resource,'Ex>> =
        async {
            try
                try
                    let! res = server.Retrieval
                    return Success res
                finally
                    stopwatch.Stop()
            with
            | ex ->

                // because if an exception happens roughly at the same time as cancellation, we don't care so much
                let isLateEnoughToReportProblem (cancelledAt: Option<DateTime>) =
                    match cancelledAt with
                    | None -> false
                    | Some date ->
                        (date + TimeSpan.FromSeconds 1.) < DateTime.UtcNow

                let report = Config.DebugLog &&
                             shouldReportUncancelledJobs &&
                             internallyCancelled.SafeDo(fun x -> isLateEnoughToReportProblem x.Value)

                let maybeSpecificEx = FSharpUtil.FindException<'Ex> ex
                match maybeSpecificEx with
                | Some specificInnerEx ->
                    if report then
                        Console.Error.WriteLine (sprintf "Cancellation fault warning: %s"
                                                     (ex.ToString()))
                    return Error specificInnerEx
                | None ->
                    return raise (FSharpUtil.ReRaise ex)
        }

type FaultTolerantParallelClient<'K,'E when 'K: equality and 'K :> ICommunicationHistory and 'E :> Exception>
        (updateServer: ('K->bool)->HistoryFact->unit) =
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

    let rec WhenSomeInternal (consistencySettings: Option<ConsistencySettings<'R>>)
                             (initialServerCount: uint32)
                             (tasks: List<Task<NonParallelResults<'K,'R,'E>>>)
                             (resultsSoFar: List<'R>)
                             (failedFuncsSoFar: List<UnsuccessfulServer<'K,'R,'E>>)
                                 : Async<FinalResult<'K,'T,'R,'E>> = async {
        match tasks with
        | [] ->
            return
                InconsistentOrNotEnoughResults
                    {
                        SuccessfulResults = resultsSoFar
                        UnsuccessfulServers = failedFuncsSoFar
                    }
        | theTasks ->

            let taskToWaitForFirstFinishedTask = Task.WhenAny theTasks
            let! fastestTask = Async.AwaitTask taskToWaitForFirstFinishedTask

            let restOfTasks: List<Task<NonParallelResults<'K,'R,'E>>> =
                theTasks.Where(fun task -> not (Object.ReferenceEquals(task, fastestTask))) |> List.ofSeq

            let newResults =
                match fastestTask.Result.PossibleResult with
                | None ->
                    resultsSoFar
                | Some newResult ->
                    newResult::resultsSoFar

            let newRestOfTasks =
                match fastestTask.Result.PendingWork with
                | None ->
                    restOfTasks
                | Some unlaunchedJobWithMoreTasks ->
                    let newTask = Async.StartAsTask unlaunchedJobWithMoreTasks
                    newTask::restOfTasks

            let newFailedFuncs = List.append failedFuncsSoFar fastestTask.Result.Failures

            let returnWithConsistencyOf (minNumberOfConsistentResultsRequired: Option<uint32>) cacheMatchFunc = async {
                let resultsSortedByCount = MeasureConsistency newResults
                match resultsSortedByCount with
                | [] ->
                    return! WhenSomeInternal consistencySettings
                                             initialServerCount
                                             newRestOfTasks newResults newFailedFuncs
                | (mostConsistentResult,maxNumberOfConsistentResultsObtained)::_ ->
                    match minNumberOfConsistentResultsRequired,cacheMatchFunc with
                    | None, None ->
                        return ConsistentResult mostConsistentResult
                    | Some number, Some cacheMatch ->
                        if cacheMatch mostConsistentResult || (maxNumberOfConsistentResultsObtained = int number) then
                            return ConsistentResult mostConsistentResult
                        else
                            return! WhenSomeInternal consistencySettings
                                                     initialServerCount
                                                     newRestOfTasks
                                                     newResults
                                                     newFailedFuncs
                    | _ -> return failwith "should be either both None or both Some!"
            }

            match consistencySettings with
            | Some (AverageBetweenResponses (minimumNumberOfResponses,averageFunc)) ->
                if (newResults.Length >= int minimumNumberOfResponses) then
                    return AverageResult (averageFunc newResults)
                else
                    return! WhenSomeInternal consistencySettings
                                             initialServerCount
                                             newRestOfTasks
                                             newResults
                                             newFailedFuncs
            | Some (SpecificNumberOfConsistentResponsesRequired number) ->
                return! returnWithConsistencyOf (Some number) ((fun _ -> false) |> Some)
            | Some (OneServerConsistentWithCacheOrTwoServers cacheMatchFunc) ->
                return! returnWithConsistencyOf (Some 2u) (Some cacheMatchFunc)
            | None ->
                if newRestOfTasks.Length = 0 then

                    if Config.DebugLog then
                        Console.WriteLine "100% done (for this currency)"
                    return! returnWithConsistencyOf None None

                else
                    if Config.DebugLog &&

                       // even when all funcs have been finished, we still have newRestOfTasks.Length==1
                       // because of the way ConcatenateNonParallelFuncs works with empty([]) servers var
                       not (newFailedFuncs.Length + newResults.Length = int initialServerCount) then

                        Console.WriteLine(sprintf "%f%% done (for this currency)"
                            (100.*(float (newFailedFuncs.Length+newResults.Length))/(float initialServerCount)))

                    return! WhenSomeInternal consistencySettings
                                             initialServerCount
                                             newRestOfTasks
                                             newResults
                                             newFailedFuncs
    }

    // at the time of writing this, I only found a Task.WhenAny() equivalent function in the asyncF# world, called
    // "Async.WhenAny" in TomasP's tryJoinads source code, however it seemed a bit complex for me to wrap my head around
    // it (and I couldn't just consume it and call it a day, I had to modify it to be "WhenSome" instead of "WhenAny",
    // as in when N>1), so I decided to write my own, using Tasks to make sure I would not spawn duplicate jobs
    let WhenSome (consistencySettings: Option<ConsistencySettings<'R>>)
                 (initialServerCount: uint32)
                 (jobs: List<Async<NonParallelResults<'K,'R,'E>>>)
                 (resultsSoFar: List<'R>)
                 (failedFuncsSoFar: List<UnsuccessfulServer<'K,'R,'E>>)
                 (cancellationSource: CancellationTokenSource)
                     : Async<FinalResult<'K,'T,'R,'E>> =
        let tasks = LaunchAsyncJobs jobs cancellationSource
        WhenSomeInternal consistencySettings initialServerCount tasks resultsSoFar failedFuncsSoFar

    let rec ConcatenateNonParallelFuncs (failuresSoFar: List<UnsuccessfulServer<'K,'R,'E>>)
                                        (shouldReportUncancelledJobs: bool)
                                        (cancelledInternally: MutableStateCapsule<Option<DateTime>>)
                                        (servers: List<Server<'K,'R>>)
                                            : Async<NonParallelResults<'K,'R,'E>> =
        match servers with
        | [] ->
            async {
                return {
                    PossibleResult = None
                    Failures = failuresSoFar
                    PendingWork = None
                }
            }
        | head::tail ->
            async {
                let stopwatch = Stopwatch()
                stopwatch.Start()
                let! runResult = Runner.Run head stopwatch cancelledInternally shouldReportUncancelledJobs

                match runResult with
                | Success result ->
                    let historyFact = { TimeSpan = stopwatch.Elapsed; Fault = None }
                    updateServer (fun server -> server = head.Details) historyFact
                    let tailAsync =
                        ConcatenateNonParallelFuncs failuresSoFar shouldReportUncancelledJobs cancelledInternally tail
                    return {
                        PossibleResult = Some result
                        Failures = failuresSoFar
                        PendingWork = Some tailAsync
                    }

                | Error ex ->
                    let exInfo =
                        {
                            TypeFullName = ex.GetType().FullName
                            Message = ex.Message
                        }
                    let historyFact = { TimeSpan = stopwatch.Elapsed; Fault = (Some exInfo) }
                    updateServer (fun server -> server = head.Details) historyFact
                    let newFailures = { Server = head; Failure = ex }::failuresSoFar
                    return! ConcatenateNonParallelFuncs newFailures shouldReportUncancelledJobs cancelledInternally tail
            }

    let CancelAndDispose (source: CancellationTokenSource)
                         (cancelledInternally: MutableStateCapsule<Option<DateTime>>) =
        cancelledInternally.SafeDo(
            fun cancelledInternallyState ->
                if cancelledInternallyState.Value.IsNone then
                    try
                        source.Cancel()
                        cancelledInternallyState.Value <- Some DateTime.UtcNow
                        source.Dispose()
                    with
                    | :? ObjectDisposedException ->
                        ()
        )

    let rec QueryInternalImplementation
                          (settings: FaultTolerantParallelClientSettings<'R>)
                          (initialFuncCount: uint32)
                          (funcs: List<Server<'K,'R>>)
                          (resultsSoFar: List<'R>)
                          (failedFuncsSoFar: List<UnsuccessfulServer<'K,'R,'E>>)
                          (retries: uint32)
                          (retriesForInconsistency: uint32)
                          (cancellationSource: CancellationTokenSource)
                          (cancelledInternally: MutableStateCapsule<Option<DateTime>>)
                              : Async<'R> = async {
        if not (funcs.Any()) then
            return raise(ArgumentException("number of funcs must be higher than zero",
                                           "funcs"))
        let howManyFuncs = uint32 funcs.Length
        let numberOfParallelJobsAllowed = int settings.NumberOfParallelJobsAllowed

        match settings.ResultSelectionMode with
        | Selective resultSelectionSettings ->
            match resultSelectionSettings.ConsistencyConfig with
            | SpecificNumberOfConsistentResponsesRequired numberOfConsistentResponsesRequired ->
                if numberOfConsistentResponsesRequired < 1u then
                    return raise <| ArgumentException("must be higher than zero", "numberOfConsistentResponsesRequired")
                if (howManyFuncs < numberOfConsistentResponsesRequired) then
                    return raise(ArgumentException("number of funcs must be equal or higher than numberOfConsistentResponsesRequired",
                                                   "funcs"))
            | AverageBetweenResponses(minimumNumberOfResponses,averageFunc) ->
                if (int minimumNumberOfResponses > numberOfParallelJobsAllowed) then
                    return raise(ArgumentException("numberOfParallelJobsAllowed should be equal or higher than minimumNumberOfResponses for the averageFunc",
                                                   "settings"))
            | OneServerConsistentWithCacheOrTwoServers _ ->
                ()
        | _ -> ()

        let funcsToRunInParallel,restOfFuncs =
            if (howManyFuncs > settings.NumberOfParallelJobsAllowed) then
                funcs |> Seq.take numberOfParallelJobsAllowed, funcs |> Seq.skip numberOfParallelJobsAllowed
            else
                funcs |> Seq.ofList, Seq.empty

        let shouldReportUncancelledJobs =
            match settings.ResultSelectionMode with
            | Exhaustive -> false
            | Selective subSettings ->
                subSettings.ReportUncancelledJobs

        // each bucket can be run in parallel, each bucket contains 1 or more funcs that cannot be run in parallel
        // e.g. if we have funcs A, B, C, D and numberOfParallelJobsAllowed=2, then we have funcBucket1(A,B) and
        //      funcBucket2(C,D), then fb1&fb2 are started at the same time (A&C start at the same time), and B
        //      starts only when A finishes or fails, and D only starts when C finishes or fails
        let funcBuckets =
            Seq.splitInto numberOfParallelJobsAllowed funcs
            |> Seq.map List.ofArray
            |> Seq.map
                (ConcatenateNonParallelFuncs List.Empty shouldReportUncancelledJobs cancelledInternally)
            |> List.ofSeq

        let lengthOfBucketsSanityCheck = Math.Min(funcs.Length, numberOfParallelJobsAllowed)
        if (lengthOfBucketsSanityCheck <> funcBuckets.Length) then
            return failwithf "Assertion failed, splitInto didn't work as expected? got %d, should be %d"
                             funcBuckets.Length lengthOfBucketsSanityCheck

        let consistencyConfig =
            match settings.ResultSelectionMode with
            | Exhaustive -> None
            | Selective subSettings -> Some subSettings.ConsistencyConfig
        let! result =
            WhenSome consistencyConfig initialFuncCount funcBuckets resultsSoFar failedFuncsSoFar cancellationSource
        match result with
        | AverageResult averageResult ->
            CancelAndDispose cancellationSource cancelledInternally
            return averageResult
        | ConsistentResult consistentResult ->
            CancelAndDispose cancellationSource cancelledInternally
            return consistentResult
        | InconsistentOrNotEnoughResults executedServers ->
            let failedFuncs = executedServers.UnsuccessfulServers
                                  |> List.map (fun unsuccessfulServer -> unsuccessfulServer.Server)
            if executedServers.SuccessfulResults.Length = 0 then
                if (retries = settings.NumberOfRetries) then
                    let firstEx = executedServers.UnsuccessfulServers.First().Failure
                    CancelAndDispose cancellationSource cancelledInternally
                    return raise (NoneAvailableException("Not available", firstEx))
                else
                    return! QueryInternalImplementation
                                          settings
                                          initialFuncCount
                                          failedFuncs
                                          executedServers.SuccessfulResults
                                          List.Empty
                                          (retries + 1u)
                                          retriesForInconsistency
                                          cancellationSource
                                          cancelledInternally
            else
                let totalNumberOfSuccesfulResultsObtained = executedServers.SuccessfulResults.Length

                // HACK: we do this as a quick fix wrt new OneServerConsistentWithCacheOrTwoServers setting, but we should
                // (TODO) rather throw a specific overload of ResultInconsistencyException about this mode being used
                let wrappedSettings =
                    match consistencyConfig with
                    | Some (OneServerConsistentWithCacheOrTwoServers _) ->
                        Some (SpecificNumberOfConsistentResponsesRequired 2u)
                    | _ -> consistencyConfig

                match wrappedSettings with
                | Some (SpecificNumberOfConsistentResponsesRequired numberOfConsistentResponsesRequired) ->
                    let resultsOrderedByCount = MeasureConsistency executedServers.SuccessfulResults
                    match resultsOrderedByCount with
                    | [] ->
                        return failwith "resultsSoFar.Length != 0 but MeasureConsistency returns None, please report this bug"
                    | (mostConsistentResult,maxNumberOfConsistentResultsObtained)::_ ->
                        if (retriesForInconsistency = settings.NumberOfRetriesForInconsistency) then
                            CancelAndDispose cancellationSource cancelledInternally
                            return raise (ResultInconsistencyException(totalNumberOfSuccesfulResultsObtained,
                                                                       maxNumberOfConsistentResultsObtained,
                                                                       numberOfConsistentResponsesRequired))
                        else
                            return! QueryInternalImplementation
                                                  settings
                                                  initialFuncCount
                                                  funcs
                                                  List.Empty
                                                  List.Empty
                                                  retries
                                                  (retriesForInconsistency + 1u)
                                                  cancellationSource
                                                  cancelledInternally
                | Some(AverageBetweenResponses(minimumNumberOfResponses,averageFunc)) ->
                    if (retries = settings.NumberOfRetries) then
                        let firstEx = executedServers.UnsuccessfulServers.First().Failure
                        CancelAndDispose cancellationSource cancelledInternally
                        return raise (NotEnoughAvailableException("resultsSoFar.Length != 0 but not enough to satisfy minimum number of results for averaging func", firstEx))
                    else
                        return! QueryInternalImplementation
                                              settings
                                              initialFuncCount
                                              failedFuncs
                                              executedServers.SuccessfulResults
                                              executedServers.UnsuccessfulServers
                                              (retries + 1u)
                                              retriesForInconsistency
                                              cancellationSource
                                              cancelledInternally
                | _ ->
                    return failwith "wrapping settings didn't work?"

    }

    let SortServers (servers: List<Server<'K,'R>>) (mode: ServerSelectionMode): List<Server<'K,'R>> =
        let workingServers = List.filter (fun server ->
                                             match server.Details.CommunicationHistory with
                                             | None ->
                                                 false
                                             | Some historyInfo ->
                                                 match historyInfo.Status with
                                                 | Fault _ ->
                                                     false
                                                 | _ ->
                                                     true
                                         ) servers
        let sortedWorkingServers =
            List.sortBy
                (fun server ->
                    match server.Details.CommunicationHistory with
                    | None ->
                        failwith "previous filter didn't work? should get working servers only, not lacking history"
                    | Some historyInfo ->
                        match historyInfo.Status with
                        | Fault _ ->
                            failwith "previous filter didn't work? should get working servers only, not faulty"
                        | _ ->
                            historyInfo.TimeSpan
                )
                workingServers

        let serversWithNoHistoryServers = List.filter (fun server -> server.Details.CommunicationHistory.IsNone) servers

        let faultyServers = List.filter (fun server ->
                                            match server.Details.CommunicationHistory with
                                            | None ->
                                                false
                                            | Some historyInfo ->
                                                match historyInfo.Status with
                                                | Fault _ ->
                                                    true
                                                | _ ->
                                                    false
                                        ) servers
        let sortedFaultyServers =
            List.sortBy
                (fun server ->
                    match server.Details.CommunicationHistory with
                    | None ->
                        failwith "previous filter didn't work? should get working servers only, not lacking history"
                    | Some historyInfo ->
                        match historyInfo.Status with
                        | Fault _ ->
                            historyInfo.TimeSpan
                        | _ ->
                            failwith "previous filter didn't work? should get faulty servers only, not working ones"
                )
                faultyServers

        if mode = ServerSelectionMode.Fast then
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
        if settings.NumberOfParallelJobsAllowed < 1u then
            raise (ArgumentException("must be higher than zero", "numberOfParallelJobsAllowed"))

        let effectiveCancellationSource =
            match cancellationTokenSourceOption with
            | None ->
                new CancellationTokenSource()
            | Some cancellationSource ->
                cancellationSource

        let cancelledInternally = MutableStateCapsule<Option<DateTime>> None

        let initialServerCount = uint32 servers.Length
        let maybeSortedServers =
            match settings.ResultSelectionMode with
            | Exhaustive -> servers
            | Selective selSettings ->
                SortServers servers selSettings.ServerSelectionMode

        QueryInternalImplementation
            settings
            initialServerCount
            maybeSortedServers
            List.Empty
            List.Empty
            0u
            0u
            effectiveCancellationSource
            cancelledInternally

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
