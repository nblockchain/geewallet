namespace GWallet.Backend

open System

module FaultTolerantClient =

    type NoneAvailableException (message:string, lastException: Exception) =
       inherit Exception (message, lastException)

    let public Query<'T,'R> (args: 'T) (funcs: list<'T->'R>): 'R =
        let rec queryInternal (args: 'T) (lastEx: Exception) (funcs: list<'T->'R>) =
            match funcs with
            | [] -> raise (NoneAvailableException("Not available", lastEx))
            | head::tail ->
                try
                    head(args)
                with
                | ex -> queryInternal args ex tail

        queryInternal args null funcs
