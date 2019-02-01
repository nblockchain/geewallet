namespace GWallet.Backend

open System
open System.Runtime.ExceptionServices
open Microsoft.FSharp.Reflection

module FSharpUtil =

    type internal ResultWrapper<'T>(value : 'T) =

        // hack?
        inherit Exception()

        member self.Value = value

    // taken from http://fssnip.net/dN ( https://stackoverflow.com/a/20521059/544947 )
    module AsyncExtensions =

        // efficient raise
        let private RaiseResult (e: ResultWrapper<'T>) =
            Async.FromContinuations(fun (_, econt, _) -> econt e)

        let Choice<'T>(jobs: seq<Async<Option<'T>>>) : Async<'T option> =
            let wrap job =
                async {
                    let! res = job
                    match res with
                    | None -> return None
                    | Some r ->
                        return! RaiseResult <| ResultWrapper r
                }

            async {
                try
                    do!
                        jobs
                        |> Seq.map wrap
                        |> Async.Parallel
                        |> Async.Ignore

                    return None
                with
                | :? ResultWrapper<'T> as ex ->
                    return Some ex.Value
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

    let WithTimeout (timeSpan: TimeSpan) (operation: Async<'R>): Async<Option<'R>> = async {
        let! child = Async.StartChild (operation, int timeSpan.TotalMilliseconds)
        try
            let! result = child
            return Some result
        with :? TimeoutException ->
            return None
    }

    // FIXME: we should not need this workaround anymore when this gets addressed:
    //        https://github.com/fsharp/fslang-suggestions/issues/660
    let ReRaise (ex: Exception): Exception =
        (ExceptionDispatchInfo.Capture ex).Throw ()
        failwith "Should be unreachable"
        ex

    let rec public FindException<'T when 'T:> Exception>(ex: Exception): Option<'T> =
        if null = ex then
            None
        else
            match ex with
            | :? 'T as specificEx -> Some(specificEx)
            | _ -> FindException<'T>(ex.InnerException)

// http://stackoverflow.com/a/28466431/6503091
    // will crash if 'T contains members which aren't only tags
    let Construct<'T> (caseInfo: UnionCaseInfo) = FSharpValue.MakeUnion(caseInfo, [||]) :?> 'T

    let GetUnionCaseInfoAndInstance<'T> (caseInfo: UnionCaseInfo) = (Construct<'T> caseInfo)

    let GetAllElementsFromDiscriminatedUnion<'T>() =
        FSharpType.GetUnionCases(typeof<'T>)
        |> Seq.map GetUnionCaseInfoAndInstance<'T>
