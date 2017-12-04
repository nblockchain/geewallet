namespace GWallet.Backend

open System

module FaultTolerantClient =

    type NoneAvailableException (message:string, lastException: Exception) =
       inherit Exception (message, lastException)

    let public Query<'T,'R,'E when 'E :> Exception> (args: 'T) (funcs: list<'T->'R>): 'R =
        if typeof<'E> = typeof<Exception> then
            raise (ArgumentException("'E cannot be System.Exception, use a derived one", "'E"))
        let rec queryInternal (args: 'T) (lastEx: Exception) (funcs: list<'T->'R>) =
            match funcs with
            | [] -> raise (NoneAvailableException("Not available", lastEx))
            | head::tail ->
                try
                    head(args)
                with
                | :? 'E as ex ->
                    if (Config.DebugLog) then
                        Console.Error.WriteLine (sprintf "Fault warning: %s: %s"
                                                     (ex.GetType().FullName)
                                                     ex.Message)
                    queryInternal args ex tail

        queryInternal args null funcs
