namespace GWallet.Backend

open System
open System.Linq
open System.Threading.Tasks
open System.Runtime.ExceptionServices
#if STRICTER_COMPILATION_BUT_WITH_REFLECTION_AT_RUNTIME
open Microsoft.FSharp.Reflection
#endif


module FSharpUtil =

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

        let SPrintF6 (fmt: string) (a: Object) (b: Object) (c: Object) (d: Object) (e: Object) (f: Object) =
            String.Format(ToStringFormat fmt, a, b, c, d, e, f)

    module UwpHacks =
#if STRICTER_COMPILATION_BUT_WITH_REFLECTION_AT_RUNTIME
        let SPrintF1 fmt a = sprintf fmt a

        let SPrintF2 fmt a b = sprintf fmt a b

        let SPrintF3 fmt a b c = sprintf fmt a b c

        let SPrintF4 fmt a b c d = sprintf fmt a b c d

        let SPrintF5 fmt a b c d e = sprintf fmt a b c d e

        let SPrintF6 fmt a b c d e f = sprintf fmt a b c d e f
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

        let SPrintF6 (fmt: string) (a: Object) (b: Object) (c: Object) (d: Object) (e: Object) (f: Object) =
            ReflectionlessPrint.SPrintF6 fmt a b c d e f
#endif


#if STRICTER_COMPILATION_BUT_WITH_REFLECTION_AT_RUNTIME
// http://stackoverflow.com/a/28466431/6503091
    // will crash if 'T contains members which aren't only tags
    let Construct<'T> (caseInfo: UnionCaseInfo) = FSharpValue.MakeUnion(caseInfo, [||]) :?> 'T

    let GetUnionCaseInfoAndInstance<'T> (caseInfo: UnionCaseInfo) = (Construct<'T> caseInfo)

    let GetAllElementsFromDiscriminatedUnion<'T>() =
        FSharpType.GetUnionCases(typeof<'T>)
        |> Seq.map GetUnionCaseInfoAndInstance<'T>
#endif
