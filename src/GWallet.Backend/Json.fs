namespace GWallet.Backend.Json

open System.IO
open Newtonsoft.Json
open ResultUtils.Portability

type ParseJsonError =
    | UnexpectedToken of JsonToken
    | UnexpectedEOF
    | MalformedObject
    with
    override self.ToString() =
        match self with
        | UnexpectedToken token -> "unexpected token: " + (token.ToString())
        | UnexpectedEOF -> "unexpected EOF"
        | MalformedObject -> "malformed object"

type JsonValue =
    | Null
    | Bool of bool
    | Number of decimal
    | String of string
    | Array of list<JsonValue>
    | Object of Map<string, JsonValue>

    static member Parse (text: string): Result<JsonValue, ParseJsonError> =
        let rec takeValue (reader: JsonTextReader): Result<JsonValue, ParseJsonError> =
            match reader.TokenType with
            | JsonToken.Null ->
                Ok JsonValue.Null
            | JsonToken.Boolean ->
                Ok <| JsonValue.Bool (reader.Value :?> bool)
            | JsonToken.Integer ->
                Ok <| JsonValue.Number (decimal (reader.Value :?> int64))
            | JsonToken.Float ->
                Ok <| JsonValue.Number (decimal (reader.Value :?> double))
            | JsonToken.String ->
                Ok <| JsonValue.String (reader.Value :?> string)
            | JsonToken.StartArray ->
                match parseArray List.empty reader with
                | Error err -> Error err
                | Ok arrayValue -> Ok <| JsonValue.Array arrayValue
            | JsonToken.StartObject ->
                match parseObject Map.empty reader with
                | Error err -> Error err
                | Ok objectValue -> Ok <| JsonValue.Object objectValue
            | _ -> Error (ParseJsonError.UnexpectedToken reader.TokenType)

        and parseValue (reader: JsonTextReader): Result<JsonValue, ParseJsonError> =
            if reader.Read() then
                takeValue reader
            else
                Error ParseJsonError.UnexpectedEOF

        and parseArray (acc: list<JsonValue>) (reader: JsonTextReader): Result<list<JsonValue>, ParseJsonError> =
            if reader.Read() then
                match reader.TokenType with
                | JsonToken.EndArray -> Ok acc
                | _ ->
                    match takeValue reader with
                    | Error err -> Error err
                    | Ok value -> parseArray (List.append acc [value]) reader
            else
                Error ParseJsonError.UnexpectedEOF

        and parseObject (acc: Map<string, JsonValue>) (reader: JsonTextReader): Result<Map<string, JsonValue>, ParseJsonError> =
            if reader.Read() then
                match reader.TokenType with
                | JsonToken.EndObject -> Ok acc
                | JsonToken.PropertyName ->
                    let key = reader.Value :?> string
                    match parseValue reader with
                    | Error err -> Error err
                    | Ok value -> parseObject (Map.add key value acc) reader
                | _ -> Error ParseJsonError.MalformedObject
            else
                Error ParseJsonError.UnexpectedEOF

        use reader = new JsonTextReader(new StringReader(text))
        parseValue reader

