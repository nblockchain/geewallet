namespace GWallet.Backend

open System.ComponentModel

// this attribute below is for Json.NET (Newtonsoft.Json) to be able to deserialize this as a dict key
[<TypeConverter(typeof<StringTypeConverter>)>]
type Currency =
    | BTC
    | LTC
    | ETH
    | ETC
    | DAI
    static member ToStrings() =
        Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(typeof<Currency>)
            |> Array.map (fun info -> info.Name)
    static member GetAll(): seq<Currency> =
        FSharpUtil.GetAllElementsFromDiscriminatedUnion<Currency>()
    member self.IsEther() =
        self = Currency.ETC || self = Currency.ETH
    member self.IsEthToken() =
        self = Currency.DAI
    member self.IsEtherBased() =
        self.IsEther() || self.IsEthToken()
    member self.IsUtxo() =
        self = Currency.BTC || self = Currency.LTC
    override self.ToString() =
        sprintf "%A" self

// the reason we have used "and" is because of the circular reference
// between StringTypeConverter and Currency
and private StringTypeConverter() =
    inherit TypeConverter()
    override this.CanConvertFrom(context, sourceType) =
        sourceType = typeof<string> || base.CanConvertFrom(context, sourceType)
    override this.ConvertFrom(context, culture, value) =
        match value with
        | :? string as stringValue ->
            Seq.find (fun cur -> cur.ToString() = stringValue) (Currency.GetAll()) :> obj
        | _ -> base.ConvertFrom(context, culture, value)
