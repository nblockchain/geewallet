namespace GWallet.Backend

open System
open System.Linq
open System.Threading.Tasks

type NoneAvailableException (message:string, lastException: Exception) =
   inherit Exception (message, lastException)


type ResultInconsistencyException (totalNumberOfSuccesfulResultsObtained: int,
                                   maxNumberOfConsistentResultsObtained: int,
                                   numberOfConsistentResultsRequired: int) =
  inherit Exception ("Results obtained were not enough to be considered consistent" +
                      sprintf " (received: %d, consistent: %d, required: %d)"
                                  totalNumberOfSuccesfulResultsObtained
                                  maxNumberOfConsistentResultsObtained
                                  numberOfConsistentResultsRequired)

type internal ResultsSoFar<'R> = List<'R>
type internal ExceptionsSoFar<'T,'R,'E when 'E :> Exception> = List<('T->'R)*'E>
type internal ConsistencyResult<'T,'R,'E when 'E :> Exception> =
    | ConsistentResult of 'R
    | InconsistentOrNotEnoughResults of ResultsSoFar<'R>*ExceptionsSoFar<'T,'R,'E>

type internal NonParallelResultWithAdditionalWork<'T,'R,'E when 'E :> Exception> =
    | SuccessfulFirstResult of ('R * Async<NonParallelResults<'T,'R,'E>>)
    | NoneAvailable
and internal NonParallelResults<'T,'R,'E when 'E :> Exception> =
    ExceptionsSoFar<'T,'R,'E> * NonParallelResultWithAdditionalWork<'T,'R,'E>

type FaultTolerantParallelClient<'E when 'E :> Exception>(numberOfConsistentResponsesRequired: int,
                                                          numberOfMaximumParallelJobs: int,
                                                          numberOfRetries: uint16,
                                                          numberOfRetriesForInconsistency: uint16) =
    do
        if typeof<'E> = typeof<Exception> then
            raise (ArgumentException("'E cannot be System.Exception, use a derived one", "'E"))
        if numberOfConsistentResponsesRequired < 1 then
            raise (ArgumentException("must be higher than zero", "numberOfConsistentResponsesRequired"))
        if numberOfMaximumParallelJobs < 1 then
            raise (ArgumentException("must be higher than zero", "numberOfMaximumParallelJobs"))

    let MeasureConsistency (results: List<'R>) =
        results |> Seq.countBy id |> Seq.sortByDescending (fun (_,count: int) -> count) |> List.ofSeq

    let LaunchAsyncJobs(jobs:List<Async<NonParallelResults<'T,'R,'E>>>): List<Task<NonParallelResults<'T,'R,'E>>> =
        jobs |> Seq.map (fun asyncJob -> Async.StartAsTask asyncJob) |> List.ofSeq

    let rec WhenSomeInternal (numberOfResultsRequired: int)
                             (tasks: List<Task<NonParallelResults<'T,'R,'E>>>)
                             (resultsSoFar: List<'R>)
                             (failedFuncsSoFar: ExceptionsSoFar<'T,'R,'E>)
                             : Async<ConsistencyResult<'T,'R,'E>> = async {
        match tasks with
        | [] ->
            return InconsistentOrNotEnoughResults(resultsSoFar,failedFuncsSoFar)
        | theTasks ->

            let taskToWaitForFirstFinishedTask = Task.WhenAny theTasks
            let! fastestTask = Async.AwaitTask taskToWaitForFirstFinishedTask
            let failuresOfTask,resultOfTask = fastestTask.Result

            let restOfTasks: List<Task<NonParallelResults<'T,'R,'E>>> =
                theTasks.Where(fun task -> not (Object.ReferenceEquals(task, fastestTask))) |> List.ofSeq

            let (newResults,newRestOfTasks) =
                match resultOfTask with
                | SuccessfulFirstResult(newResult,unlaunchedJobWithMoreTasks) ->
                    let newTask = Async.StartAsTask unlaunchedJobWithMoreTasks
                    (newResult::resultsSoFar),(newTask::restOfTasks)
                | NoneAvailable ->
                    resultsSoFar,restOfTasks
            let newFailedFuncs = List.append failedFuncsSoFar failuresOfTask

            let resultsSortedByCount = MeasureConsistency newResults
            match resultsSortedByCount with
            | [] ->
                return! WhenSomeInternal numberOfResultsRequired newRestOfTasks newResults newFailedFuncs
            | (mostConsistentResult,maxNumberOfConsistentResultsObtained)::_ ->
                if (maxNumberOfConsistentResultsObtained = numberOfResultsRequired) then
                    return ConsistentResult mostConsistentResult
                else
                    return! WhenSomeInternal numberOfResultsRequired newRestOfTasks newResults newFailedFuncs
    }

    // at the time of writing this, I only found a Task.WhenAny() equivalent function in the asyncF# world, called
    // "Async.WhenAny" in TomasP's tryJoinads source code, however it seemed a bit complex for me to wrap my head around
    // it (and I couldn't just consume it and call it a day, I had to modify it to be "WhenSome" instead of "WhenAny",
    // as in when N>1), so I decided to write my own, using Tasks to make sure I would not spawn duplicate jobs
    let WhenSome (numberOfConsistentResultsRequired: int)
                 (jobs: List<Async<NonParallelResults<'T,'R,'E>>>)
                 (resultsSoFar: List<'R>)
                 (failedFuncsSoFar: ExceptionsSoFar<'T,'R,'E>)
                 : Async<ConsistencyResult<'T,'R,'E>> =
        let tasks = LaunchAsyncJobs jobs
        WhenSomeInternal numberOfConsistentResultsRequired tasks resultsSoFar failedFuncsSoFar

    let rec ConcatenateNonParallelFuncs (args: 'T) (failuresSoFar: ExceptionsSoFar<'T,'R,'E>) (funcs: List<'T->'R>)
                                        : Async<NonParallelResults<'T,'R,'E>> =
        match funcs with
        | [] ->
            async {
                return failuresSoFar,NoneAvailable
            }
        | head::tail ->
            async {
                try
                    let result = head args
                    let tailAsync = ConcatenateNonParallelFuncs args failuresSoFar tail
                    return failuresSoFar,SuccessfulFirstResult(result,tailAsync)
                with
                | :? 'E as ex ->
                    if (Config.DebugLog) then
                        Console.Error.WriteLine (sprintf "Fault warning: %s: %s"
                                                     (ex.GetType().FullName)
                                                     ex.Message)
                    let newFailures = (head,ex)::failuresSoFar
                    return! ConcatenateNonParallelFuncs args newFailures tail
            }

    let rec QueryInternal (args: 'T)
                          (funcs: List<'T->'R>)
                          (resultsSoFar: List<'R>)
                          (failedFuncsSoFar: ExceptionsSoFar<'T,'R,'E>)
                          (retries: uint16)
                          (retriesForInconsistency: uint16)
                              : Async<'R> = async {
        if not (funcs.Any()) then
            return raise(ArgumentException("number of funcs must be higher than zero",
                                           "funcs"))
        if (funcs.Count() < numberOfConsistentResponsesRequired) then
            return raise(ArgumentException("number of funcs must be equal or higher than numberOfConsistentResponsesRequired",
                                           "funcs"))

        let funcsToRunInParallel,restOfFuncs =
            if (funcs.Length > numberOfMaximumParallelJobs) then
                funcs |> Seq.take numberOfMaximumParallelJobs, funcs |> Seq.skip numberOfMaximumParallelJobs
            else
                funcs |> Seq.ofList, Seq.empty

        // each bucket can be run in parallel, each bucket contains 1 or more funcs that cannot be run in parallel
        let funcBuckets =
            Seq.splitInto numberOfMaximumParallelJobs funcs
            |> Seq.map List.ofArray
            |> Seq.map (ConcatenateNonParallelFuncs args List.empty)
            |> List.ofSeq

        if (funcBuckets.Length <> numberOfMaximumParallelJobs) then
            return failwithf "Assertion failed, splitInto didn't work as expected? got %d, should be %d"
                             funcBuckets.Length numberOfMaximumParallelJobs

        let! result =
            WhenSome numberOfConsistentResponsesRequired funcBuckets resultsSoFar failedFuncsSoFar
        match result with
        | ConsistentResult consistentResult ->
            return consistentResult
        | InconsistentOrNotEnoughResults(allResultsSoFar,failedFuncsWithTheirExceptions) ->

            if (allResultsSoFar.Length = 0) then
                if (retries = numberOfRetries) then
                    let firstEx = failedFuncsWithTheirExceptions.First() |> snd
                    return raise (NoneAvailableException("Not available", firstEx))
                else
                    let failedFuncs: List<'T->'R> = failedFuncsWithTheirExceptions |> List.map fst
                    return! QueryInternal args
                                          failedFuncs
                                          allResultsSoFar
                                          []
                                          (uint16 (retries + uint16 1))
                                          retriesForInconsistency
            else
                let totalNumberOfSuccesfulResultsObtained = allResultsSoFar.Length
                let resultsOrderedByCount = MeasureConsistency allResultsSoFar
                match resultsOrderedByCount with
                | [] ->
                    return failwith "resultsSoFar.Length != 0 but MeasureConsistency returns None, please report this bug"
                | (mostConsistentResult,maxNumberOfConsistentResultsObtained)::_ ->
                    if (retriesForInconsistency = numberOfRetriesForInconsistency) then
                        return raise (ResultInconsistencyException(totalNumberOfSuccesfulResultsObtained,
                                                                   maxNumberOfConsistentResultsObtained,
                                                                   numberOfConsistentResponsesRequired))
                    else
                        return! QueryInternal args
                                              funcs
                                              []
                                              []
                                              retries
                                              (uint16 (retriesForInconsistency + uint16 1))
    }

    member self.Query<'T,'R when 'R : equality> (args: 'T) (funcs: list<'T->'R>): Async<'R> =

        QueryInternal args funcs [] [] (uint16 0) (uint16 0)
