namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Linq

open NBitcoin

exception TemplateInstantiationException of string

type IvyTemplateScript(script: string) =
    let opPrefix = "OP_"

    let rec ReplaceOpCodes (opCodes: List<OpcodeType>) (acc: string): string =
        match opCodes with
        | [] -> acc
        | head::tail ->
            if (head.ToString().StartsWith opPrefix) then
                let opCodeWithoutPrefix = head.ToString().Substring opPrefix.Length

                match Int32.TryParse opCodeWithoutPrefix with
                | true, _ ->
                    // because we don't want "0" converted to OP_0, etc.
                    ReplaceOpCodes tail acc
                | false, _ ->
                    let newAcc = acc.Replace(opCodeWithoutPrefix, head.ToString())
                    ReplaceOpCodes tail newAcc
            else
                ReplaceOpCodes tail acc

    let rec ReplacePushesWithValues (values) (acc: string): string =
        match values with
        | [] ->
            acc
        | (key,value)::tail ->
            let push = sprintf "PUSH(%s)" key
            if not (acc.Contains(push)) then
                raise (TemplateInstantiationException (sprintf "Key %s not found in a push" key))
            let replaced = acc.Replace(push, value)
            ReplacePushesWithValues tail replaced

    let CheckValueInstantiationIsComplete (script: string): unit =
        if (script.Contains("PUSH(")) then
            raise (TemplateInstantiationException "Not enough values provided for this template")

    member this.Instantiate(values: Map<string,string>): string =
        let opCodes: seq<OpcodeType> = (Enum.GetValues typeof<OpcodeType>).Cast<OpcodeType>()
        let opCodesList = opCodes |> Seq.toList
        let scriptCopy = script
        let scriptWithValuesReplaced = ReplacePushesWithValues (Map.toList values) scriptCopy
        CheckValueInstantiationIsComplete scriptWithValuesReplaced
        ReplaceOpCodes opCodesList scriptWithValuesReplaced
