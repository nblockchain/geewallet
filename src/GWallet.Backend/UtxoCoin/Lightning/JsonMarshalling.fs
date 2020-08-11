namespace GWallet.Backend.UtxoCoin.Lightning

open System
open Newtonsoft.Json

open GWallet.Backend

module JsonMarshalling =
    type internal ChannelIdentifierConverter() =
        inherit JsonConverter<ChannelIdentifier>()

        override this.ReadJson(reader: JsonReader, _: Type, _: ChannelIdentifier, _: bool, serializer: JsonSerializer) =
            let serializedChannelId = serializer.Deserialize<string> reader
            serializedChannelId
            |> NBitcoin.uint256
            |> DotNetLightning.Utils.ChannelId
            |> ChannelIdentifier.FromDnl

        override this.WriteJson(writer: JsonWriter, state: ChannelIdentifier, serializer: JsonSerializer) =
            let serializedChannelId: string = state.DnlChannelId.Value.ToString()
            serializer.Serialize(writer, serializedChannelId)

    let internal SerializerSettings: JsonSerializerSettings =
        let settings = Marshalling.DefaultSettings ()
        settings.Converters.Add <| ChannelIdentifierConverter()
        DotNetLightning.JsonMarshalling.RegisterConverters settings
        settings

