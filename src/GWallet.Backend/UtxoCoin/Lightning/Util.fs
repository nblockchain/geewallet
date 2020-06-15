namespace GWallet.Backend.UtxoCoin.Lightning

open GWallet.Backend.FSharpUtil.UwpHacks

open FSharp.Core

type IErrorMsg =
    abstract member Message: string

module Util =
    let Unwrap<'T, 'TError>(result: Result<'T, 'TError>)
                           (msg: string)
                               : 'T =
        match result with
        | Ok value -> value
        | Error err ->
            failwith <| SPrintF2 "error unwrapping Result: %s: %s" msg (err.ToString())

    let UnwrapOption<'T>(opt: Option<'T>)
                        (msg: string)
                            : 'T =
        match opt with
        | Some value -> value
        | None ->
            failwith <| SPrintF1 "error unwrapping Option: %s" msg

