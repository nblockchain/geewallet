namespace GWallet.Backend

open System
open System.Linq

open Newtonsoft.Json
open Newtonsoft.Json.Linq

// this attribute below is for Json.NET (Newtonsoft.Json) to be able to deserialize this as a dict key
[<JsonConverter(typeof<CurrencyConverter>)>]
type Currency =
    | ETH
    | ETC
    static member GetAll(): seq<Currency> =
        FSharpUtil.GetAllElementsFromDiscriminatedUnion<Currency>()
    override self.ToString() =
        sprintf "%A" self

// the reason we have used "and" is because of the circular reference
// between StringTypeConverter and Currency
and private CurrencyConverter() =
    inherit JsonConverter()

    override this.CanConvert(objectType): bool =
        objectType = typedefof<Currency>

    override this.ReadJson(reader: JsonReader, objectType: Type, existingValue: Object, serializer: JsonSerializer) =
        if (reader.TokenType = JsonToken.Null) then
            null
        else
            let token =
                JToken.Load(reader)
                      // not sure about the below way to convert to string, in stackoverflow it was a C# cast
                      .ToString()
            try
                Currency.GetAll().First(fun currency -> currency.ToString() = token) :> Object
            with ex -> raise(new Exception(sprintf "Currency not found: %s" token, ex))

    override this.WriteJson(writer: JsonWriter, value: Object, serializer: JsonSerializer) =
        let currency = value :?> Currency
        writer.WriteValue(currency.ToString())

