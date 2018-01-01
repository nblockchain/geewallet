namespace GWallet.Backend

open System

module Shuffler =

    let private random = Random()

    let Unsort aSeq =
        aSeq |> Seq.sortBy (fun _ -> random.Next())
