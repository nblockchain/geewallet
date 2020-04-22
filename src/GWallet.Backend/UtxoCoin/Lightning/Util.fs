namespace GWallet.Backend.UtxoCoin.Lightning

open GWallet.Backend.FSharpUtil.UwpHacks

module Util = 
    let Unwrap<'T, 'TError>(result: FSharp.Core.Result<'T, 'TError>)
                           (msg: string)
                               : 'T =
        match result with
        | Ok value -> value
        | Error err ->
            failwith <| SPrintF2 "error unwrapping Result: %s: %s" msg (err.ToString())


