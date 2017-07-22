namespace GWallet.Backend

open System

module FaultTolerantClient =

    exception NotAvailable

    let public Query<'T,'R> (args: 'T) (funcs: list<'T->'R>): 'R =
        let rec queryInternal (args: 'T) (funcs: list<'T->'R>) =
            match funcs with
            | [] -> raise NotAvailable
            | head::tail ->
                try
                    head(args)
                with
                | _ -> queryInternal args tail

        queryInternal args funcs