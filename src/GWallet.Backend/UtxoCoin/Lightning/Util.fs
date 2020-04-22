namespace GWallet.Backend.UtxoCoin.Lightning

open GWallet.Backend
open GWallet.Backend.UtxoCoin
open GWallet.Backend.FSharpUtil.UwpHacks
open FSharp.Core

module Util = 
    let Unwrap<'T, 'TError>(result: FSharp.Core.Result<'T, 'TError>)
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

    let QueryBTCFast (command: (Async<StratumClient> -> Async<'T>)): Async<'T> =
        Server.Query
            Currency.BTC
            (QuerySettings.Default ServerSelectionMode.Fast)
            command
            None
