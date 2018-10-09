namespace GWallet.Backend

open System
open System.Runtime.ExceptionServices
open Microsoft.FSharp.Reflection

module FSharpUtil =

    let WithTimeout (timeSpan: TimeSpan) operation = async {
        let! child = Async.StartChild (operation, int timeSpan.TotalMilliseconds)
        try
            let! result = child
            return Some result
        with :? TimeoutException -> return None
    }

    // FIXME: we should not need this workaround anymore when this gets addressed:
    //        https://github.com/fsharp/fslang-suggestions/issues/660
    let ReRaise (ex: Exception): Exception =
            (ExceptionDispatchInfo.Capture ex).Throw ()
            failwith "Should be unreachable"
            ex

    let rec public FindExceptionThat(ex: Exception) (predicate: Exception -> bool): Option<Exception> =
        if (ex = null) then
            None
        else
            if predicate ex then
                Some ex
            else
                FindExceptionThat ex.InnerException predicate

    let rec public FindException<'T when 'T:> Exception>(ex: Exception): Option<'T> =
        if (ex = null) then
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
