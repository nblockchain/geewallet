namespace GWallet.Backend

open System
open System.Linq
open System.Threading.Tasks
open System.Runtime.ExceptionServices
#if STRICTER_COMPILATION_BUT_WITH_REFLECTION_AT_RUNTIME
open Microsoft.FSharp.Reflection
#endif

type Result<'Val, 'Err when 'Err :> Exception> =
    | Error of 'Err
    | Value of 'Val

module FSharpUtil =

    [<DefaultAugmentation(false)>]
    type Maybe<'T> =
        | Nothing
        | Just of 'T
        member self.IsJust =
            match self with
            | Nothing -> false
            | _ -> true
        member self.IsNothing =
            match self with
            | Nothing -> true
            | _ -> false
        member self.ToOpt() =
            match self with
            | Nothing -> None
            | Just x -> Some x

    module Maybe =
        let Exists (func: _ -> bool) (opt: Maybe<_>): bool =
            match opt with
            | Nothing -> false
            | Just x -> func x
        let OfOpt (from: Option<_>): Maybe<_> =
            match from with
            | None -> Nothing
            | Some x -> Just x
        let OfObj from =
            Option.ofObj from |> OfOpt
        // taken from https://fsharpforfunandprofit.com/posts/elevated-world-2/#implementation-examples
        let Bind f xOpt =
            match xOpt with
            | Just x -> f x
            | _ -> Nothing
        let ToAsyncOpt (job: Async<Maybe<_>>): Async<Option<_>> =
            async {
                let! foo = job
                match foo with
                | Nothing -> return None
                | Just bar -> return Some bar
            }

    module ReflectionlessPrint =
        // TODO: support "%.2f" for digits precision, "%0i", and other special things: https://fsharpforfunandprofit.com/posts/printf/
        let ToStringFormat (fmt: string) =
            let rec inner (innerFmt: string) (count: uint32) =
                // TODO: support %e, %E, %g, %G, %o, %O, %x, %X, etc. see link above
                let supportedFormats = [| "%s"; "%d"; "%i"; "%u"; "%M"; "%f"; "%b" ; "%A"; |]
                let formatsFound = supportedFormats.Where(fun format -> innerFmt.IndexOf(format) >= 0)
                if formatsFound.Any() then
                    let firstIndexWhereFormatFound = formatsFound.Min(fun format -> innerFmt.IndexOf(format))
                    let firstFormat =
                        formatsFound.First(fun format -> innerFmt.IndexOf(format) = firstIndexWhereFormatFound)
                    let subEnd = innerFmt.IndexOf(firstFormat) + "%x".Length
                    let sub = innerFmt.Substring(0, subEnd)
                    let x = sub.Replace(firstFormat, "{" + count.ToString() + "}") + innerFmt.Substring(subEnd)
                    inner x (count+1u)
                else
                    innerFmt
            (inner fmt 0u).Replace("%%", "%")

        let SPrintF1 (fmt: string) (a: Object) =
            String.Format(ToStringFormat fmt, a)

        let SPrintF2 (fmt: string) (a: Object) (b: Object) =
            String.Format(ToStringFormat fmt, a, b)

        let SPrintF3 (fmt: string) (a: Object) (b: Object) (c: Object) =
            String.Format(ToStringFormat fmt, a, b, c)

        let SPrintF4 (fmt: string) (a: Object) (b: Object) (c: Object) (d: Object) =
            String.Format(ToStringFormat fmt, a, b, c, d)

        let SPrintF5 (fmt: string) (a: Object) (b: Object) (c: Object) (d: Object) (e: Object) =
            String.Format(ToStringFormat fmt, a, b, c, d, e)


    module UwpHacks =
#if STRICTER_COMPILATION_BUT_WITH_REFLECTION_AT_RUNTIME
        let SPrintF1 fmt a = sprintf fmt a

        let SPrintF2 fmt a b = sprintf fmt a b

        let SPrintF3 fmt a b c = sprintf fmt a b c

        let SPrintF4 fmt a b c d = sprintf fmt a b c d

        let SPrintF5 fmt a b c d e = sprintf fmt a b c d e
#else
        let SPrintF1 (fmt: string) (a: Object) =
            ReflectionlessPrint.SPrintF1 fmt a

        let SPrintF2 (fmt: string) (a: Object) (b: Object) =
            ReflectionlessPrint.SPrintF2 fmt a b

        let SPrintF3 (fmt: string) (a: Object) (b: Object) (c: Object) =
            ReflectionlessPrint.SPrintF3 fmt a b c

        let SPrintF4 (fmt: string) (a: Object) (b: Object) (c: Object) (d: Object) =
            ReflectionlessPrint.SPrintF4 fmt a b c d

        let SPrintF5 (fmt: string) (a: Object) (b: Object) (c: Object) (d: Object) (e: Object) =
            ReflectionlessPrint.SPrintF5 fmt a b c d e
#endif

    type internal ResultWrapper<'T>(value : 'T) =

        // hack?
        inherit Exception()

        member self.Value = value

    module AsyncExtensions =

        let MixedParallel2 (a: Async<'T1>) (b: Async<'T2>): Async<'T1*'T2> =
            async {
                let aJob = Async.StartChild a
                let bJob = Async.StartChild b

                let! aStartedJob = aJob
                let! bStartedJob = bJob

                let! aJobResult = aStartedJob
                let! bJobResult = bStartedJob

                return aJobResult,bJobResult
            }

        let MixedParallel3 (a: Async<'T1>) (b: Async<'T2>) (c: Async<'T3>): Async<'T1*'T2*'T3> =
            async {
                let aJob = Async.StartChild a
                let bJob = Async.StartChild b
                let cJob = Async.StartChild c

                let! aStartedJob = aJob
                let! bStartedJob = bJob
                let! cStartedJob = cJob

                let! aJobResult = aStartedJob
                let! bJobResult = bStartedJob
                let! cJobResult = cStartedJob

                return aJobResult,bJobResult,cJobResult
            }

        // efficient raise
        let private RaiseResult (e: ResultWrapper<'T>) =
            Async.FromContinuations(fun (_, econt, _) -> econt e)

        // like Async.Choice, but with no need for Option<T> types
        let WhenAny<'T>(jobs: seq<Async<'T>>): Async<'T> =
            let wrap (job: Async<'T>): Async<Option<'T>> =
                async {
                    let! res = job
                    return Some res
                }

            async {
                let wrappedJobs = jobs |> Seq.map wrap
                let! combinedRes = Async.Choice wrappedJobs
                match combinedRes with
                | Some x ->
                    return x
                | None ->
                    return failwith "unreachable"
            }

        // a mix between Async.WhenAny and Async.Choice
        let WhenAnyAndAll<'T>(jobs: seq<Async<'T>>): Async<Async<array<'T>>> =
            let taskSource = TaskCompletionSource<unit>()
            let wrap (job: Async<'T>) =
                async {
                    let! res = job
                    taskSource.TrySetResult() |> ignore
                    return res
                }
            async {
                let allJobsInParallel =
                    jobs
                        |> Seq.map wrap
                        |> Async.Parallel
                        |> Async.StartChild
                let! allJobsStarted = allJobsInParallel
                let! _ = Async.AwaitTask taskSource.Task
                return allJobsStarted
            }

    let rec private ListIntersectInternal list1 list2 offset acc currentIndex =
        match list1,list2 with
        | [],[] -> List.rev acc
        | [],_ -> List.append (List.rev acc) list2
        | _,[] -> List.append (List.rev acc) list1
        | head1::tail1,head2::tail2 ->
            if currentIndex % (int offset) = 0 then
                ListIntersectInternal list1 tail2 offset (head2::acc) (currentIndex + 1)
            else
                ListIntersectInternal tail1 list2 offset (head1::acc) (currentIndex + 1)

    let ListIntersect<'T> (list1: List<'T>) (list2: List<'T>) (offset: uint32): List<'T> =
        ListIntersectInternal list1 list2 offset [] 1

    let WithTimeout (timeSpan: TimeSpan) (job: Async<'R>): Async<Maybe<'R>> = async {
        let read = async {
            let! value = job
            return value |> Value |> Some
        }

        let delay = async {
            let total = int timeSpan.TotalMilliseconds
            do! Async.Sleep total
            return Some (Error(TimeoutException()))
        }

        let! dummyOption = Async.Choice([read; delay])
        match dummyOption with
        | Some theResult ->
            match theResult with
            | Value r ->
                return Just r
            | Error _ ->
                return Nothing
        | None ->
            // none of the jobs passed to Async.Choice returns None
            return failwith "unreachable"
    }

    // FIXME: we should not need this workaround anymore when this gets addressed:
    //        https://github.com/fsharp/fslang-suggestions/issues/660
    let ReRaise (ex: Exception): Exception =
        (ExceptionDispatchInfo.Capture ex).Throw ()
        failwith "Should be unreachable"
        ex

    let rec public FindException<'T when 'T:> Exception>(ex: Exception): Maybe<'T> =
        let rec findExInSeq(sq: seq<Exception>) =
            match Seq.tryHead sq |> Maybe.OfOpt with
            | Just head ->
                let found = FindException head
                match found with
                | Just ex -> Just ex
                | Nothing ->
                    findExInSeq <| Seq.tail sq
            | Nothing ->
                Nothing
        if null = ex then
            Nothing
        else
            match ex with
            | :? 'T as specificEx -> Just specificEx
            | :? AggregateException as aggEx ->
                findExInSeq aggEx.InnerExceptions
            | _ -> FindException<'T>(ex.InnerException)


#if STRICTER_COMPILATION_BUT_WITH_REFLECTION_AT_RUNTIME
// http://stackoverflow.com/a/28466431/6503091
    // will crash if 'T contains members which aren't only tags
    let Construct<'T> (caseInfo: UnionCaseInfo) = FSharpValue.MakeUnion(caseInfo, [||]) :?> 'T

    let GetUnionCaseInfoAndInstance<'T> (caseInfo: UnionCaseInfo) = (Construct<'T> caseInfo)

    let GetAllElementsFromDiscriminatedUnion<'T>() =
        FSharpType.GetUnionCases(typeof<'T>)
        |> Seq.map GetUnionCaseInfoAndInstance<'T>
#endif

    type MaybeBuilder() =
        // see https://github.com/dsyme/fsharp-presentations/blob/master/design-notes/ces-compared.md#overview-of-f-computation-expressions
        member x.Bind (v,f) = Maybe.Bind f v
        member x.Return v = Just v
        member x.ReturnFrom o = o
        member x.Zero () = Nothing

    let maybe = MaybeBuilder()
