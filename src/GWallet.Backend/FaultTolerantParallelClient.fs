namespace GWallet.Backend

open System
open System.Linq
open System.Diagnostics
open System.Threading.Tasks

type ServerUnavailabilityException (message:string, lastException: Exception) =
    inherit Exception (message, lastException)

type private NoneAvailableException (message:string, lastException: Exception) =
   inherit ServerUnavailabilityException (message, lastException)

type private NotEnoughAvailableException (message:string, lastException: Exception) =
   inherit ServerUnavailabilityException (message, lastException)

type ResultInconsistencyException (totalNumberOfSuccesfulResultsObtained: int,
                                   maxNumberOfConsistentResultsObtained: int,
                                   numberOfConsistentResultsRequired: uint16) =
  inherit Exception ("Results obtained were not enough to be considered consistent" +
                      sprintf " (received: %d, consistent: %d, required: %d)"
                                  totalNumberOfSuccesfulResultsObtained
                                  maxNumberOfConsistentResultsObtained
                                  numberOfConsistentResultsRequired)

type internal ResultsSoFar<'R> = List<'R>
type internal ExceptionsSoFar<'K,'T,'R,'E when 'K: equality and 'E :> Exception> = List<Server<'K,'T,'R>*'E>
type internal FinalResult<'K,'T,'R,'E when 'K: equality and 'E :> Exception> =
    | ConsistentResult of 'R
    | AverageResult of 'R
    | InconsistentOrNotEnoughResults of ResultsSoFar<'R>*ExceptionsSoFar<'K,'T,'R,'E>

type internal NonParallelResultWithAdditionalWork<'K,'T,'R,'E when 'K: equality and 'E :> Exception> =
    | SuccessfulFirstResult of ('R * Async<NonParallelResults<'K,'T,'R,'E>>)
    | NoneAvailable
and internal NonParallelResults<'K,'T,'R,'E when 'K: equality and 'E :> Exception> =
    ExceptionsSoFar<'K,'T,'R,'E> * NonParallelResultWithAdditionalWork<'K,'T,'R,'E>

type ConsistencySettings<'R> =
    | NumberOfConsistentResponsesRequired of uint16
    | AverageBetweenResponses of (uint16 * (List<'R> -> 'R))

type FaultTolerantParallelClientSettings<'R> =
    {
        NumberOfMaximumParallelJobs: uint16;
        ConsistencyConfig: ConsistencySettings<'R>;
        NumberOfRetries: uint16;
        NumberOfRetriesForInconsistency: uint16;
    }

type Mode =
    | Fast
    | Analysis

type FaultTolerantParallelClient<'K,'E when 'K: equality and 'E :> Exception>(updateServer: 'K*HistoryInfo -> unit) =
    do
        if typeof<'E> = typeof<Exception> then
            raise (ArgumentException("'E cannot be System.Exception, use a derived one", "'E"))

    let MeasureConsistency (results: List<'R>) =
        results |> Seq.countBy id |> Seq.sortByDescending (fun (_,count: int) -> count) |> List.ofSeq

    let LaunchAsyncJobs(jobs:List<Async<NonParallelResults<'K,'T,'R,'E>>>): List<Task<NonParallelResults<'K,'T,'R,'E>>> =
        jobs |> List.map Async.StartAsTask

    let rec WhenSomeInternal (consistencySettings: ConsistencySettings<'R>)
                             (tasks: List<Task<NonParallelResults<'K,'T,'R,'E>>>)
                             (resultsSoFar: List<'R>)
                             (failedFuncsSoFar: ExceptionsSoFar<'K,'T,'R,'E>)
                             : Async<FinalResult<'K,'T,'R,'E>> = async {
        match tasks with
        | [] ->
            return InconsistentOrNotEnoughResults(resultsSoFar,failedFuncsSoFar)
        | theTasks ->

            let taskToWaitForFirstFinishedTask = Task.WhenAny theTasks
            let! fastestTask = Async.AwaitTask taskToWaitForFirstFinishedTask
            let failuresOfTask,resultOfTask = fastestTask.Result

            let restOfTasks: List<Task<NonParallelResults<'K,'T,'R,'E>>> =
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
                 (jobs: List<Async<NonParallelResults<'K,'T,'R,'E>>>)
                 (resultsSoFar: List<'R>)
                 (failedFuncsSoFar: ExceptionsSoFar<'K,'T,'R,'E>)
                 : Async<FinalResult<'K,'T,'R,'E>> =
        let tasks = LaunchAsyncJobs jobs
        WhenSomeInternal consistencySettings tasks resultsSoFar failedFuncsSoFar

    let rec ConcatenateNonParallelFuncs (args: 'T)
                                        (failuresSoFar: ExceptionsSoFar<'K,'T,'R,'E>)
                                        (servers: List<Server<'K,'T,'R>>)
                                        : Async<NonParallelResults<'K,'T,'R,'E>> =
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
                    let result = head.Retreival args
                    stopwatch.Stop()
                    updateServer (head.Identifier, { Fault = None; TimeSpan = stopwatch.Elapsed })
                    let tailAsync = ConcatenateNonParallelFuncs args failuresSoFar tail
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
                        let exInfo = { Type = specificInnerEx.GetType(); Message = specificInnerEx.Message }
                        updateServer (head.Identifier, { Fault = Some exInfo; TimeSpan = stopwatch.Elapsed })
                        let newFailures = (head,specificInnerEx)::failuresSoFar
                        return! ConcatenateNonParallelFuncs args newFailures tail
                    | None ->
                        return raise (FSharpUtil.ReRaise ex)
            }

    let rec QueryInternal (settings: FaultTolerantParallelClientSettings<'R>)
                          (args: 'T)
                          (funcs: List<Server<'K,'T,'R>>)
                          (resultsSoFar: List<'R>)
                          (failedFuncsSoFar: ExceptionsSoFar<'K,'T,'R,'E>)
                          (retries: uint16)
                          (retriesForInconsistency: uint16)
                              : Async<'R> = async {
        if not (funcs.Any()) then
            return raise(ArgumentException("number of funcs must be higher than zero",
                                           "funcs"))
        let howManyFuncs = uint16 funcs.Length
        let numberOfMaximumParallelJobs = int settings.NumberOfMaximumParallelJobs

        match settings.ConsistencyConfig with
        | NumberOfConsistentResponsesRequired numberOfConsistentResponsesRequired ->
            if numberOfConsistentResponsesRequired < uint16 1 then
                raise (ArgumentException("must be higher than zero", "numberOfConsistentResponsesRequired"))
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
            |> Seq.map (ConcatenateNonParallelFuncs args List.empty)
            |> List.ofSeq

        let lengthOfBucketsSanityCheck = Math.Min(funcs.Length, numberOfMaximumParallelJobs)
        if (lengthOfBucketsSanityCheck <> funcBuckets.Length) then
            return failwithf "Assertion failed, splitInto didn't work as expected? got %d, should be %d"
                             funcBuckets.Length lengthOfBucketsSanityCheck

        let! result =
            WhenSome settings.ConsistencyConfig funcBuckets resultsSoFar failedFuncsSoFar
        match result with
        | AverageResult averageResult ->
            return averageResult
        | ConsistentResult consistentResult ->
            return consistentResult
        | InconsistentOrNotEnoughResults(allResultsSoFar,failedFuncsWithTheirExceptions) ->
            let failedFuncs = failedFuncsWithTheirExceptions |> List.map fst
            if (allResultsSoFar.Length = 0) then
                if (retries = settings.NumberOfRetries) then
                    let firstEx = failedFuncsWithTheirExceptions.First() |> snd
                    return raise (NoneAvailableException("Not available", firstEx))
                else
                    return! QueryInternal settings
                                          args
                                          failedFuncs
                                          allResultsSoFar
                                          List.Empty
                                          (uint16 (retries + uint16 1))
                                          retriesForInconsistency
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


                            return raise (ResultInconsistencyException(totalNumberOfSuccesfulResultsObtained,
                                                                       maxNumberOfConsistentResultsObtained,
                                                                       numberOfConsistentResponsesRequired))
                        else
                            return! QueryInternal settings
                                                  args
                                                  funcs
                                                  List.Empty
                                                  List.Empty
                                                  retries
                                                  (uint16 (retriesForInconsistency + uint16 1))
                | AverageBetweenResponses(minimumNumberOfResponses,averageFunc) ->
                    if (retries = settings.NumberOfRetries) then
                        let firstEx = failedFuncsWithTheirExceptions.First() |> snd
                        return raise (NotEnoughAvailableException("resultsSoFar.Length != 0 but not enough to satisfy minimum number of results for averaging func", firstEx))
                    else
                        return! QueryInternal settings
                                              args
                                              failedFuncs
                                              allResultsSoFar
                                              failedFuncsWithTheirExceptions
                                              (uint16 (retries + uint16 1))
                                              retriesForInconsistency

    }

    let OrderServers (servers: List<Server<'K,'T,'R>>) (mode: Mode): List<Server<'K,'T,'R>> =
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

        // FIXME: sort faulty servers as well (it's better to query the ones that fail fast than the slow-failers)
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

        if mode = Mode.Fast then
            List.append sortedWorkingServers (List.append serversWithNoHistoryServers faultyServers)
        else
            let intersectionOffset = (uint16 3)
            let result = FSharpUtil.ListIntersect
                                     (List.append serversWithNoHistoryServers sortedWorkingServers)
                                     faultyServers
                                     intersectionOffset
            let randomizationOffset = intersectionOffset + (uint16 1)
            Shuffler.RandomizeEveryNthElement result randomizationOffset

    member self.Query<'T,'R when 'R : equality> (settings: FaultTolerantParallelClientSettings<'R>)
                                                (args: 'T)
                                                (servers: List<Server<'K,'T,'R>>)
                                                (mode: Mode)
                                                    : Async<'R> =
        if settings.NumberOfMaximumParallelJobs < uint16 1 then
            raise (ArgumentException("must be higher than zero", "numberOfMaximumParallelJobs"))

        QueryInternal settings args (OrderServers servers mode) List.Empty List.Empty (uint16 0) (uint16 0)
