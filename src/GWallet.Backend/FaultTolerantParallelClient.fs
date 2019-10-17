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

// TODO: remove this below once we finishing tracking down (fixing) https://gitlab.com/knocte/geewallet/issues/125
type UnexpectedTaskCanceledException(message: string, innerException) =
    inherit TaskCanceledException (message, innerException)

type ResultInconsistencyException (totalNumberOfSuccesfulResultsObtained: int,
                                   maxNumberOfConsistentResultsObtained: int,
                                   numberOfConsistentResultsRequired: uint32) =
  inherit Exception ("Results obtained were not enough to be considered consistent" +
                      sprintf " (received: %d, consistent: %d, required: %d)"
                                  totalNumberOfSuccesfulResultsObtained
                                  maxNumberOfConsistentResultsObtained
                                  numberOfConsistentResultsRequired)

type UnsuccessfulServer<'K,'R when 'K: equality and 'K :> ICommunicationHistory> =
    {
        Server: Server<'K,'R>
        Failure: Exception
    }
type ExecutedServers<'K,'R when 'K: equality and 'K :> ICommunicationHistory> =
    {
        SuccessfulResults: List<'R>
        UnsuccessfulServers: List<UnsuccessfulServer<'K,'R>>
    }
type internal FinalResult<'K,'T,'R when 'K: equality and 'K :> ICommunicationHistory> =
    | ConsistentResult of 'R
    | AverageResult of 'R
    | InconsistentOrNotEnoughResults of ExecutedServers<'K,'R>

type internal ServerResult<'K,'R when 'K: equality and 'K :> ICommunicationHistory> =
    | SuccessfulResult of 'R
    | Failure of UnsuccessfulServer<'K,'R>

type ConsistencySettings<'R> =

    // fun passed represents if cached value matches or not
    | OneServerConsistentWithCertainValueOrTwoServers of ('R->bool)

    | SpecificNumberOfConsistentResponsesRequired of uint32
    | AverageBetweenResponses of (uint32 * (List<'R> -> 'R))

type ServerSelectionMode =
    | Fast
    | Analysis

type ResultSelectionSettings<'R> =
    {
        ServerSelectionMode: ServerSelectionMode
        ReportUncanceledJobs: bool
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
        ExceptionHandler: Option<Exception->unit>
    }

type Result<'Val, 'Err when 'Err :> Exception> =
    | Error of 'Err
    | Value of 'Val

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

type internal ServerJob<'K,'R when 'K: equality and 'K :> ICommunicationHistory> =
    {
        Job: Async<ServerResult<'K,'R>>
        Server: Server<'K,'R>
    }

type internal ServerTask<'K,'R when 'K: equality and 'K :> ICommunicationHistory> =
    {
        Task: Task<ServerResult<'K,'R>>
        Server: Server<'K,'R>
        CancellationTokenSource: CancellationTokenSource
    }
    with
        static member WhenAny(tasks: seq<ServerTask<'K,'R>>) =
            async {
                let task = Task.WhenAny(tasks.Select(fun t -> t.Task))
                let! fastestTask = Async.AwaitTask task
                let correspondingTask = tasks.Single(fun t -> t.Task = fastestTask)
                return correspondingTask
            }

type Runner<'Resource when 'Resource: equality> =
    static member Run<'K,'Ex when 'K: equality and 'K :> ICommunicationHistory and 'Ex :> Exception>
                      (server: Server<'K,'Resource>)
                      (stopwatch: Stopwatch)
                      (internallyCanceled: MutableStateCapsule<Option<DateTime>>)
                      (shouldReportUncanceledJobs: bool)
                      (maybeExceptionHandler: Option<Exception->unit>)
                          : Async<Result<'Resource,Exception>> =
        async {
            try
                try
                    let! res = server.Retrieval
                    return Value res
                finally
                    stopwatch.Stop()
            with
            | ex ->

                // because if an exception happens roughly at the same time as cancellation, we don't care so much
                let isLateEnoughToReportProblem (canceledAt: Option<DateTime>) =
                    match canceledAt with
                    | None -> false
                    | Some date ->
                        (date + TimeSpan.FromSeconds 1.) < DateTime.UtcNow

                let report = Config.DebugLog &&
                             shouldReportUncanceledJobs &&
                             internallyCanceled.SafeDo(fun x -> isLateEnoughToReportProblem x.Value)

                let maybeSpecificEx = FSharpUtil.FindException<'Ex> ex
                match maybeSpecificEx with
                | Some specificInnerEx ->
                    if report then
                        Console.Error.WriteLine (sprintf "Cancellation fault warning: %s"
                                                     (ex.ToString()))
                    return Error (specificInnerEx :> Exception)
                | None ->
                    match maybeExceptionHandler with
                    | None -> return raise <| FSharpUtil.ReRaise ex
                    | Some exceptionHandler ->
                        exceptionHandler ex
                        return Error ex
        }

type FaultTolerantParallelClient<'K,'E when 'K: equality and 'K :> ICommunicationHistory and 'E :> Exception>
        (updateServer: ('K->bool)->HistoryFact->unit) =
    do
        if typeof<'E> = typeof<Exception> then
            raise (ArgumentException("'E cannot be System.Exception, use a derived one", "'E"))

    let MeasureConsistency (results: List<'R>) =
        results |> Seq.countBy id |> Seq.sortByDescending (fun (_,count: int) -> count) |> List.ofSeq

    let LaunchAsyncJob (job: ServerJob<'K,'R>)
                       (cancellationSource: CancellationTokenSource)
                           : ServerTask<'K,'R> =
        let token =
            try
                cancellationSource.Token
            with
            | :? ObjectDisposedException as ex ->
                raise <| TaskUnavailabilityException("cancellationTokenSource already disposed", ex)
        let task = Async.StartAsTask(job.Job, ?cancellationToken = Some token)

        let serverTask = {
            Task = task
            Server = job.Server
            CancellationTokenSource = cancellationSource
        }

        serverTask

    let rec WhenSomeInternal (consistencySettings: Option<ConsistencySettings<'R>>)
                             (initialServerCount: uint32)
                             (startedTasks: List<ServerTask<'K,'R>>)
                             (jobsToContinueWith: List<ServerJob<'K,'R>>)
                             (resultsSoFar: List<'R>)
                             (failedFuncsSoFar: List<UnsuccessfulServer<'K,'R>>)
                             (cancellationSource: CancellationTokenSource)
                             (canceledInternally: MutableStateCapsule<Option<DateTime>>)
                                 : Async<FinalResult<'K,'T,'R>> = async {
        if startedTasks = List.Empty then
            return
                InconsistentOrNotEnoughResults
                    {
                        SuccessfulResults = resultsSoFar
                        UnsuccessfulServers = failedFuncsSoFar
                    }
        else
            let jobToWaitForFirstFinishedTask = ServerTask.WhenAny startedTasks
            let! fastestTask = jobToWaitForFirstFinishedTask

            let restOfTasks =
                startedTasks.Where(fun task -> not (task = fastestTask)) |> List.ofSeq

            let newResults,newFailedFuncs =
                try
                    match fastestTask.Task.Result with
                    | Failure unsuccessfulServer ->
                        resultsSoFar,unsuccessfulServer::failedFuncsSoFar
                    | SuccessfulResult newResult ->
                        newResult::resultsSoFar,failedFuncsSoFar
                with
                | ex when (FSharpUtil.FindException<TaskCanceledException> ex).IsSome &&
                           canceledInternally.SafeDo(fun x -> x.Value.IsNone) ->

                    let cancellationRequested = cancellationSource.IsCancellationRequested
                    let msg = sprintf "Somehow the job got canceled without being canceled internally (req?: %b)"
                                      cancellationRequested

                    // TODO: remove this below once we finishing tracking down (fixing)
                    //       https://gitlab.com/knocte/geewallet/issues/125
                    raise <| InvalidOperationException(msg, ex)
            let newRestOfTasks,newRestOfJobs =
                match jobsToContinueWith with
                | [] ->
                    restOfTasks,List.Empty
                | head::tail ->
                    let newTask = LaunchAsyncJob head cancellationSource
                    newTask::restOfTasks,tail

            let returnWithConsistencyOf (minNumberOfConsistentResultsRequired: Option<uint32>) cacheMatchFunc = async {
                let resultsSortedByCount = MeasureConsistency newResults
                match resultsSortedByCount with
                | [] ->
                    return! WhenSomeInternal consistencySettings
                                             initialServerCount
                                             newRestOfTasks
                                             newRestOfJobs
                                             newResults
                                             newFailedFuncs
                                             cancellationSource
                                             canceledInternally
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
                                                     newRestOfJobs
                                                     newResults
                                                     newFailedFuncs
                                                     cancellationSource
                                                     canceledInternally
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
                                             newRestOfJobs
                                             newResults
                                             newFailedFuncs
                                             cancellationSource
                                             canceledInternally
            | Some (SpecificNumberOfConsistentResponsesRequired number) ->
                return! returnWithConsistencyOf (Some number) ((fun _ -> false) |> Some)
            | Some (OneServerConsistentWithCertainValueOrTwoServers cacheMatchFunc) ->
                return! returnWithConsistencyOf (Some 2u) (Some cacheMatchFunc)
            | None ->
                if newRestOfTasks.Length = 0 then

                    if Config.DebugLog then
                        Console.WriteLine "100% done (for this currency)"
                    return! returnWithConsistencyOf None None

                else
                    if Config.DebugLog then
                        Console.WriteLine(sprintf "%f%% done (for this currency)"
                            (100.*(float (newFailedFuncs.Length+newResults.Length))/(float initialServerCount)))

                    return! WhenSomeInternal consistencySettings
                                             initialServerCount
                                             newRestOfTasks
                                             newRestOfJobs
                                             newResults
                                             newFailedFuncs
                                             cancellationSource
                                             canceledInternally
    }

    // at the time of writing this, I only found a Task.WhenAny() equivalent function in the asyncF# world, called
    // "Async.WhenAny" in TomasP's tryJoinads source code, however it seemed a bit complex for me to wrap my head around
    // it (and I couldn't just consume it and call it a day, I had to modify it to be "WhenSome" instead of "WhenAny",
    // as in when N>1), so I decided to write my own, using Tasks to make sure I would not spawn duplicate jobs
    let WhenSome (consistencySettings: Option<ConsistencySettings<'R>>)
                 (jobsToStart: List<ServerJob<'K,'R>>)
                 (jobsToContinueWith: List<ServerJob<'K,'R>>)
                 (resultsSoFar: List<'R>)
                 (failedFuncsSoFar: List<UnsuccessfulServer<'K,'R>>)
                 (cancellationSource: CancellationTokenSource)
                 (canceledInternally: MutableStateCapsule<Option<DateTime>>)
                     : Async<FinalResult<'K,'T,'R>> =
        let initialServerCount = jobsToStart.Length + jobsToContinueWith.Length |> uint32
        let tasks = jobsToStart |> List.map (fun job -> LaunchAsyncJob job cancellationSource)
        WhenSomeInternal consistencySettings
                         initialServerCount
                         tasks
                         jobsToContinueWith
                         resultsSoFar
                         failedFuncsSoFar
                         cancellationSource
                         canceledInternally

    let rec CreateAsyncJobFromFunc (shouldReportUncanceledJobs: bool)
                                   (canceledInternally: MutableStateCapsule<Option<DateTime>>)
                                   (exceptionHanlder: Option<Exception->unit>)
                                   (server: Server<'K,'R>)
                                       : ServerJob<'K,'R> =
        let job = async {
            let stopwatch = Stopwatch()
            stopwatch.Start()

            let! runResult =
                Runner.Run<'K,'E> server stopwatch canceledInternally shouldReportUncanceledJobs exceptionHanlder

            match runResult with
            | Value result ->
                let historyFact = { TimeSpan = stopwatch.Elapsed; Fault = None }
                updateServer (fun srv -> srv = server.Details) historyFact
                return SuccessfulResult result
            | Error ex ->
                let exInfo =
                    {
                        TypeFullName = ex.GetType().FullName
                        Message = ex.Message
                    }
                let historyFact = { TimeSpan = stopwatch.Elapsed; Fault = (Some exInfo) }
                updateServer (fun srv -> srv = server.Details) historyFact
                return
                    Failure
                        {
                            Server = server
                            Failure = ex
                        }
        }

        { Job = job; Server = server }

    let CancelAndDispose (source: CancellationTokenSource)
                         (canceledInternally: MutableStateCapsule<Option<DateTime>>) =
        canceledInternally.SafeDo(
            fun canceledInternallyState ->
                if canceledInternallyState.Value.IsNone then
                    try
                        try
                            source.Cancel()
                        with
                        | ex when (FSharpUtil.FindException<TaskCanceledException> ex).IsSome ->
                            // TODO: remove this below once we finishing tracking down (fixing)
                            //       https://gitlab.com/knocte/geewallet/issues/125
                            raise <| InvalidOperationException("FTPC cancellation causes TCE", ex)
                        canceledInternallyState.Value <- Some DateTime.UtcNow
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
                          (failedFuncsSoFar: List<UnsuccessfulServer<'K,'R>>)
                          (retries: uint32)
                          (retriesForInconsistency: uint32)
                          (cancellationSource: CancellationTokenSource)
                          (canceledInternally: MutableStateCapsule<Option<DateTime>>)
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
            | OneServerConsistentWithCertainValueOrTwoServers _ ->
                ()
        | _ -> ()

        let funcsToRunInParallel,restOfFuncs =
            if (howManyFuncs > settings.NumberOfParallelJobsAllowed) then
                funcs |> Seq.take numberOfParallelJobsAllowed, funcs |> Seq.skip numberOfParallelJobsAllowed
            else
                funcs |> Seq.ofList, Seq.empty

        let shouldReportUncanceledJobs =
            match settings.ResultSelectionMode with
            | Exhaustive -> false
            | Selective subSettings ->
                subSettings.ReportUncanceledJobs

        let parallelJobs = int settings.NumberOfParallelJobsAllowed

        let launchFunc = CreateAsyncJobFromFunc shouldReportUncanceledJobs canceledInternally settings.ExceptionHandler

        let firstJobsToLaunch = Seq.take parallelJobs funcs
                                    |> Seq.map launchFunc
                                    |> List.ofSeq
        let jobsToLaunchLater = Seq.skip parallelJobs funcs
                                    |> Seq.map launchFunc
                                    |> List.ofSeq

        let consistencyConfig =
            match settings.ResultSelectionMode with
            | Exhaustive -> None
            | Selective subSettings -> Some subSettings.ConsistencyConfig
        let! result = WhenSome consistencyConfig
                               firstJobsToLaunch
                               jobsToLaunchLater
                               resultsSoFar
                               failedFuncsSoFar
                               cancellationSource
                               canceledInternally
        match result with
        | AverageResult averageResult ->
            CancelAndDispose cancellationSource canceledInternally
            return averageResult
        | ConsistentResult consistentResult ->
            CancelAndDispose cancellationSource canceledInternally
            return consistentResult
        | InconsistentOrNotEnoughResults executedServers ->
            let failedFuncs = executedServers.UnsuccessfulServers
                                  |> List.map (fun unsuccessfulServer -> unsuccessfulServer.Server)
            if executedServers.SuccessfulResults.Length = 0 then
                if (retries = settings.NumberOfRetries) then
                    let firstEx = executedServers.UnsuccessfulServers.First().Failure
                    CancelAndDispose cancellationSource canceledInternally
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
                                          canceledInternally
            else
                let totalNumberOfSuccesfulResultsObtained = executedServers.SuccessfulResults.Length

                // HACK: we do this as a quick fix wrt new OneServerConsistentWithCertainValueOrTwoServers setting, but we should
                // (TODO) rather throw a specific overload of ResultInconsistencyException about this mode being used
                let wrappedSettings =
                    match consistencyConfig with
                    | Some (OneServerConsistentWithCertainValueOrTwoServers _) ->
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
                            CancelAndDispose cancellationSource canceledInternally
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
                                                  canceledInternally
                | Some(AverageBetweenResponses(minimumNumberOfResponses,averageFunc)) ->
                    if (retries = settings.NumberOfRetries) then
                        let firstEx = executedServers.UnsuccessfulServers.First().Failure
                        CancelAndDispose cancellationSource canceledInternally
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
                                              canceledInternally
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

        let canceledInternally = MutableStateCapsule<Option<DateTime>> None

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
            canceledInternally

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
