namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Net

open DotNetLightning.Serialize
open DotNetLightning.Utils
open DotNetLightning.Crypto

open GWallet.Backend
open FSharpUtil

open NBitcoin
open Newtonsoft.Json

module JsonMarshalling =
    type internal CommitmentPubKeyConverter() =
        inherit JsonConverter<CommitmentPubKey>()

        override this.ReadJson(reader: JsonReader, _: Type, _: CommitmentPubKey, _: bool, serializer: JsonSerializer) =
            let serializedCommitmentPubKey = serializer.Deserialize<string> reader
            let hex = NBitcoin.DataEncoders.HexEncoder()
            serializedCommitmentPubKey |> hex.DecodeData |> PubKey |> CommitmentPubKey

        override this.WriteJson(writer: JsonWriter, state: CommitmentPubKey, serializer: JsonSerializer) =
            let serializedCommitmentPubKey: string = state.PubKey.ToHex()
            serializer.Serialize(writer, serializedCommitmentPubKey)

    type internal CommitmentNumberConverter() =
        inherit JsonConverter<CommitmentNumber>()

        override this.ReadJson(reader: JsonReader, _: Type, _: CommitmentNumber, _: bool, serializer: JsonSerializer) =
            let serializedCommitmentNumber = serializer.Deserialize<uint64> reader
            CommitmentNumber <| (UInt48.MaxValue - UInt48.FromUInt64 serializedCommitmentNumber)

        override this.WriteJson(writer: JsonWriter, state: CommitmentNumber, serializer: JsonSerializer) =
            let serializedCommitmentNumber: uint64 = (UInt48.MaxValue - state.Index).UInt64
            serializer.Serialize(writer, serializedCommitmentNumber)

    type internal RevocationSetConverter() =
        inherit JsonConverter<RevocationSet>()

        override this.ReadJson(reader: JsonReader, _: Type, _: RevocationSet, _: bool, serializer: JsonSerializer) =
            let keys = serializer.Deserialize<list<CommitmentNumber * RevocationKey>> reader
            RevocationSet.FromKeys keys

        override this.WriteJson(writer: JsonWriter, state: RevocationSet, serializer: JsonSerializer) =
            let keys: list<CommitmentNumber * RevocationKey> = state.Keys
            serializer.Serialize(writer, keys)

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

    type internal FeatureBitJsonConverter() =
        inherit JsonConverter<FeatureBit>()

        override self.ReadJson(reader: JsonReader, _: Type, _: FeatureBit, _: bool, serializer: JsonSerializer) =
            let serializedFeatureBit = serializer.Deserialize<string> reader
            UnwrapResult (FeatureBit.TryParse serializedFeatureBit) "error decoding feature bit"

        override self.WriteJson(writer: JsonWriter, state: FeatureBit, serializer: JsonSerializer) =
            serializer.Serialize(writer, state.ToString())

    type internal IPAddressJsonConverter() =
        inherit JsonConverter<IPAddress>()

        override self.ReadJson(reader: JsonReader, _: Type, _: IPAddress, _: bool, serializer: JsonSerializer) =
            let serializedIPAddress = serializer.Deserialize<string> reader
            IPAddress.Parse serializedIPAddress

        override self.WriteJson(writer: JsonWriter, state: IPAddress, serializer: JsonSerializer) =
            serializer.Serialize(writer, state.ToString())

    type internal IPEndPointJsonConverter() =
        inherit JsonConverter<IPEndPoint>()

        override __.ReadJson(reader: JsonReader, _: Type, _: IPEndPoint, _: bool, serializer: JsonSerializer) =
            reader.Read() |> ignore
            let ip = serializer.Deserialize<IPAddress> reader
            reader.Read() |> ignore
            let port = serializer.Deserialize<int32> reader
            reader.Read() |> ignore
            IPEndPoint (ip, port)

        override __.WriteJson(writer: JsonWriter, state: IPEndPoint, serializer: JsonSerializer) =
            writer.WriteStartArray()
            serializer.Serialize(writer, state.Address)
            serializer.Serialize(writer, state.Port)
            writer.WriteEndArray()

    let internal SerializerSettings: JsonSerializerSettings =
        let settings = Marshalling.DefaultSettings ()
        let ipAddressConverter = IPAddressJsonConverter()
        let ipEndPointConverter = IPEndPointJsonConverter()
        let featureBitConverter = FeatureBitJsonConverter()
        let channelIdentifierConverter = ChannelIdentifierConverter()
        let commitmentNumberConverter = CommitmentNumberConverter()
        let commitmentPubKeyConverter = CommitmentPubKeyConverter()
        let revocationSetConverter = RevocationSetConverter()
        settings.Converters.Add ipAddressConverter
        settings.Converters.Add ipEndPointConverter
        settings.Converters.Add featureBitConverter
        settings.Converters.Add channelIdentifierConverter
        settings.Converters.Add commitmentNumberConverter
        settings.Converters.Add commitmentPubKeyConverter
        settings.Converters.Add revocationSetConverter
        NBitcoin.JsonConverters.Serializer.RegisterFrontConverters settings
        settings

