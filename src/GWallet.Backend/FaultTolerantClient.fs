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

type internal SuccessfulResultOrFailure<'R,'E when 'E :> Exception> =
    | SuccessfulResult of 'R
    | Failure of 'E

type internal ResultsSoFar<'R> = List<'R>
type internal ExceptionsSoFar<'T,'R,'E> = seq<('T->'R)*'E>
type internal UnfinishedTasks<'R,'E when 'E :> Exception> = seq<Task<SuccessfulResultOrFailure<'R,'E>>>
type internal ConsistencyResult<'T,'R,'E when 'E :> Exception> =
    | ConsistentResult of 'R
    | InconsistentOrNotEnoughResults of ResultsSoFar<'R>*ExceptionsSoFar<'T,'R,'E>*UnfinishedTasks<'R,'E>

type FaultTolerantClient<'E when 'E :> Exception>(numberOfConsistentResponsesRequired: int,
                                                  numberOfMaximumParallelJobs: int) =
    let NUMBER_OF_RETRIES_TO_PERFORM = uint16 1

    do
        if typeof<'E> = typeof<Exception> then
            raise (ArgumentException("'E cannot be System.Exception, use a derived one", "'E"))
        if numberOfConsistentResponsesRequired < 1 then
            raise (ArgumentException("must be higher than zero", "numberOfConsistentResponsesRequired"))
        if numberOfMaximumParallelJobs < 1 then
            raise (ArgumentException("must be higher than zero", "numberOfMaximumParallelJobs"))

    let MeasureConsistency (results: List<'R>) =
        results |> Seq.countBy id |> Seq.sortByDescending (fun (_,count: int) -> count) |> List.ofSeq

    let rec WhenSomeInternal (numberOfResultsRequired: int)
                             (tasks: List<('T->'R)*(Task<SuccessfulResultOrFailure<'R,'E>>)>)
                             (resultsSoFar: List<'R>)
                             (failedFuncsSoFar: List<('T->'R)*'E>)
                             : ConsistencyResult<'T,'R,'E> =
        match tasks with
        | [] ->
            InconsistentOrNotEnoughResults(resultsSoFar,Seq.ofList failedFuncsSoFar,Seq.empty)
        | someFuncAndTasks ->
            let theTasks = someFuncAndTasks |> Seq.map snd
            let taskToWaitForFirstFinishedTask = Task.WhenAny theTasks
            taskToWaitForFirstFinishedTask.Wait()
            let finishedFunc,finishedTask =
                (someFuncAndTasks
                    |> Seq.filter (fun (func,task) -> task = taskToWaitForFirstFinishedTask.Result)).First()

            let restOfTasks: seq<('T->'R)*(Task<SuccessfulResultOrFailure<'R,'E>>)> =
                someFuncAndTasks.Where(fun (_,task) -> not (Object.ReferenceEquals(task, finishedTask))) |> seq

            let newResults,newFailedFuncs: List<'R>*List<('T->'R)*'E> =
                match finishedTask.Result with
                | SuccessfulResult newResult ->
                    newResult::resultsSoFar,failedFuncsSoFar
                | Failure ex ->
                    resultsSoFar,(finishedFunc,ex)::failedFuncsSoFar

            let resultsSortedByCount = MeasureConsistency newResults
            match resultsSortedByCount with
            | [] ->
                WhenSomeInternal numberOfResultsRequired (restOfTasks |> List.ofSeq) newResults newFailedFuncs
            | (mostConsistentResult,maxNumberOfConsistentResultsObtained)::_ ->
                if (maxNumberOfConsistentResultsObtained = numberOfResultsRequired) then
                    ConsistentResult mostConsistentResult
                else
                    WhenSomeInternal numberOfResultsRequired (restOfTasks |> List.ofSeq) newResults newFailedFuncs

    // at the time of writing this, I only found a Task.WhenAny() equivalent function in the asyncF# world, called
    // "Async.WhenAny" in TomasP's tryJoinads source code, however it seemed a bit complex for me to wrap my head around
    // it (and I couldn't just consume it and call it a day, I had to modify it to be "WhenSome" instead of "WhenAny",
    // as in when N>1), so I decided to write my own, using Tasks to make sure I would not spawn duplicate jobs
    let WhenSome (numberOfConsistentResultsRequired: int)
                 (jobs: seq<('T->'R)*Async<SuccessfulResultOrFailure<'R,'E>>>)
                 (resultsSoFar: List<'R>)
                 (failedFuncsSoFar: List<('T->'R)*'E>)
                 : ConsistencyResult<'T,'R,'E> =
        let tasks =
            jobs
                |> Seq.map (fun (func,job) -> func,Async.StartAsTask job) |> List.ofSeq
        WhenSomeInternal numberOfConsistentResultsRequired tasks resultsSoFar failedFuncsSoFar

    let rec QueryInternal (args: 'T)
                          (funcs: List<'T->'R>)
                          (resultsSoFar: List<'R>)
                          (failedFuncsSoFar: List<('T->'R)*'E>)
                          (retries: uint16)
                              : 'R =
        match funcs with
        | [] ->
            if (resultsSoFar.Length = 0) then
                if (failedFuncsSoFar.Length = 0) then
                    failwith "No more funcs provided and no exceptions so far, please report this bug"
                if (retries = NUMBER_OF_RETRIES_TO_PERFORM) then
                    raise (NoneAvailableException("Not available", failedFuncsSoFar.First() |> snd))
                else
                    let failedFuncs: List<'T->'R> = failedFuncsSoFar |> List.map fst
                    QueryInternal args failedFuncs resultsSoFar [] (uint16 (retries + uint16 1))
            else
                let totalNumberOfSuccesfulResultsObtained = resultsSoFar.Length
                let resultsOrderedByCount = MeasureConsistency resultsSoFar
                match resultsOrderedByCount with
                | [] ->
                    failwith "resultsSoFar.Length != 0 but MeasureConsistency returns None, please report this bug"
                | (mostConsistentResult,maxNumberOfConsistentResultsObtained)::_ ->
                    raise (ResultInconsistencyException(totalNumberOfSuccesfulResultsObtained,
                                                        maxNumberOfConsistentResultsObtained,
                                                        numberOfConsistentResponsesRequired))

        | someFuncs ->

            let funcsToRunInParallel,restOfFuncs =
                if (someFuncs.Length > numberOfMaximumParallelJobs) then
                    someFuncs |> Seq.take numberOfMaximumParallelJobs, someFuncs |> Seq.skip numberOfMaximumParallelJobs
                else
                    someFuncs |> Seq.ofList, Seq.empty
            let asyncJobsToRunInParallelAsAsync =
                seq {
                    for func in funcsToRunInParallel do
                        yield func,async {
                            try
                                let result = func args
                                return SuccessfulResult result
                            with
                            | :? 'E as ex ->
                                if (Config.DebugLog) then
                                    Console.Error.WriteLine (sprintf "Fault warning: %s: %s"
                                                                 (ex.GetType().FullName)
                                                                 ex.Message)
                                return Failure ex
                        }
                }

            let result =
                WhenSome numberOfConsistentResponsesRequired asyncJobsToRunInParallelAsAsync resultsSoFar failedFuncsSoFar
            match result with
            | ConsistentResult consistentResult -> consistentResult
            | InconsistentOrNotEnoughResults(allResultsSoFar,exceptions,unfinishedTasks) ->
                let unfinishedTasksList = unfinishedTasks |> List.ofSeq
                if unfinishedTasksList.Length <> 0 then
                    failwith "Assertion failed: if results is not consistent enough, there should be no unfinished tasks"
                QueryInternal args (restOfFuncs |> List.ofSeq) allResultsSoFar (exceptions |> List.ofSeq) retries

    member self.Query<'T,'R when 'R : equality> (args: 'T) (funcs: list<'T->'R>): Async<'R> =
        if not (funcs.Any()) then
            raise(ArgumentException("number of funcs must be higher than zero",
                                    "funcs"))
        if (funcs.Count() < numberOfConsistentResponsesRequired) then
            raise(ArgumentException("number of funcs must be equal or higher than numberOfConsistentResponsesRequired",
                                    "funcs"))
        async {
            return QueryInternal args funcs [] [] (uint16 0)
        }