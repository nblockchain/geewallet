namespace GWallet.Backend

open System

type HistoryInfo =
    { TimeSpan: TimeSpan
      Fault: Option<Exception> }

[<CustomEquality; NoComparison>]
type Server<'K,'T,'R when 'K: equality> =
    { Identifier: 'K
      HistoryInfo: Option<HistoryInfo>
      Retreival: 'T -> 'R }
    override x.Equals yObj =
        match yObj with
        | :? Server<'K,'T,'R> as y ->
            x.Identifier.Equals y.Identifier
        | _ -> false
    override this.GetHashCode () =
        this.Identifier.GetHashCode()
