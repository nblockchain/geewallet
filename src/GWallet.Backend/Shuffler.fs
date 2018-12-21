namespace GWallet.Backend

type UnsafeRandom = System.Random

module Shuffler =

    let private random = UnsafeRandom()

    let Unsort aSeq =
        aSeq |> Seq.sortBy (fun _ -> random.Next())

    let private ListRemove<'T when 'T: equality> (list: List<'T>) (elementToRemove: 'T)  =
        List.filter (fun element -> element <> elementToRemove) list

    let RandomizeEveryNthElement<'T when 'T: equality> (list: List<'T>) (offset: uint16) =
        let rec RandomizeInternal (list: List<'T>) (offset: uint16) acc (currentIndex: uint16) =
            match list with
            | [] -> List.rev acc
            | head::tail ->
                let nextIndex = (currentIndex + (uint16 1))
                if currentIndex % offset <> uint16 0 || tail = [] then
                    RandomizeInternal tail offset (head::acc) nextIndex
                else
                    let randomizedRest = Unsort tail |> List.ofSeq
                    match randomizedRest with
                    | [] -> failwith "should have fallen under previous 'if' case"
                    | randomizedHead::randomizedTail ->
                        let newRest = head::(ListRemove tail randomizedHead)
                        RandomizeInternal newRest offset (randomizedHead::acc) nextIndex

        RandomizeInternal list offset [] (uint16 1)
