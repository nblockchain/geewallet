namespace GWallet.Backend

open System
open System.Reflection

open Newtonsoft.Json

type SerializableValue<'T>(value: 'T) =

    member val Version: string =
        Assembly.GetExecutingAssembly().GetName().Version.ToString() with get

    member val TypeName: string =
        typeof<'T>.FullName with get

    member val Value: 'T = value with get

type DeserializableValue<'T>() =
    let mutable version: string = null
    let mutable typeName: string = null
    let mutable value: 'T = Unchecked.defaultof<'T>

    member this.Version
        with get() = version 
        and set(valueToSet) =
            version <- valueToSet

    member this.TypeName
        with get() = typeName 
        and set(valueToSet) =
            typeName <- valueToSet

    member this.Value
        with get() = value 
        and set(valueToSet) =
            value <- valueToSet


module Marshalling =

    let Deserialize<'S,'T when 'S:> DeserializableValue<'T>>(json: string): 'T =
        if (json = null) then
            raise (ArgumentNullException("json"))
        if (String.IsNullOrWhiteSpace(json)) then
            raise (ArgumentException("empty or whitespace json", "json"))

        let deserialized: 'S = JsonConvert.DeserializeObject<'S>(json)

        // HACK: this is because comparing to null in the F# world is a clusterfuck at compile-time
        try
            deserialized.Value.ToString() |> ignore
            deserialized.Value
        with
        | :? NullReferenceException ->
            failwith ("Could not deserialize from JSON: " + json)

    let Serialize<'S>(value: 'S): string =
        JsonConvert.SerializeObject(SerializableValue<'S>(value))
