namespace GWallet.Backend

type UnsafeRandom = System.Random

module Shuffler =

    let private random = UnsafeRandom()

    let Unsort aSeq =
        aSeq |> Seq.sortBy (fun _ -> random.Next())
