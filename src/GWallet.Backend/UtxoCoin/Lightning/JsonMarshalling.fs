namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.IO
open System.Net

open NBitcoin
open DotNetLightning.Channel
open DotNetLightning.Utils
open DotNetLightning.Chain
open DotNetLightning.Crypto
open DotNetLightning.Transactions
open DotNetLightning.Serialize

open GWallet.Backend
open GWallet.Backend.UtxoCoin.Lightning.Util

open Newtonsoft.Json

type CommitmentPubKeyConverter() =
    inherit JsonConverter<CommitmentPubKey>()

    override this.ReadJson(reader: JsonReader, _: Type, _: CommitmentPubKey, _: bool, serializer: JsonSerializer) =
        let serializedCommitmentPubKey = serializer.Deserialize<string> reader
        let hex = NBitcoin.DataEncoders.HexEncoder()
        serializedCommitmentPubKey |> hex.DecodeData |> PubKey |> CommitmentPubKey

    override this.WriteJson(writer: JsonWriter, state: CommitmentPubKey, serializer: JsonSerializer) =
        let serializedCommitmentPubKey: string = state.PubKey.ToHex()
        serializer.Serialize(writer, serializedCommitmentPubKey)

type CommitmentNumberConverter() =
    inherit JsonConverter<CommitmentNumber>()

    override this.ReadJson(reader: JsonReader, _: Type, _: CommitmentNumber, _: bool, serializer: JsonSerializer) =
        let serializedCommitmentNumber = serializer.Deserialize<uint64> reader
        CommitmentNumber <| (UInt48.MaxValue - UInt48.FromUInt64 serializedCommitmentNumber)

    override this.WriteJson(writer: JsonWriter, state: CommitmentNumber, serializer: JsonSerializer) =
        let serializedCommitmentNumber: uint64 = (UInt48.MaxValue - state.Index).UInt64
        serializer.Serialize(writer, serializedCommitmentNumber)

type RevocationSetConverter() =
    inherit JsonConverter<RevocationSet>()

    override this.ReadJson(reader: JsonReader, _: Type, _: RevocationSet, _: bool, serializer: JsonSerializer) =
        let keys = serializer.Deserialize<list<CommitmentNumber * RevocationKey>> reader
        RevocationSet.FromKeys keys

    override this.WriteJson(writer: JsonWriter, state: RevocationSet, serializer: JsonSerializer) =
        let keys: list<CommitmentNumber * RevocationKey> = state.Keys
        serializer.Serialize(writer, keys)

type FeatureBitJsonConverter() =
    inherit JsonConverter<FeatureBit>()

    override this.ReadJson(reader: JsonReader, _: Type, _: FeatureBit, _: bool, serializer: JsonSerializer) =
        let serializedFeatureBit = serializer.Deserialize<string> reader
        Unwrap (FeatureBit.TryParse serializedFeatureBit) "error decoding feature bit"

    override this.WriteJson(writer: JsonWriter, state: FeatureBit, serializer: JsonSerializer) =
        serializer.Serialize(writer, state.ToString())

type IPAddressJsonConverter() =
    inherit JsonConverter<IPAddress>()

    override this.ReadJson(reader: JsonReader, _: Type, _: IPAddress, _: bool, serializer: JsonSerializer) =
        let serializedIPAddress = serializer.Deserialize<string> reader
        IPAddress.Parse serializedIPAddress

    override this.WriteJson(writer: JsonWriter, state: IPAddress, serializer: JsonSerializer) =
        serializer.Serialize(writer, state.ToString())

type IPEndPointJsonConverter() =
    inherit JsonConverter<IPEndPoint>()

    override this.ReadJson(reader: JsonReader, _: Type, _: IPEndPoint, _: bool, serializer: JsonSerializer) =
        assert (reader.TokenType = JsonToken.StartArray)
        reader.Read() |> ignore
        let ip = serializer.Deserialize<IPAddress> reader
        reader.Read() |> ignore
        let port = serializer.Deserialize<int32> reader
        assert (reader.TokenType = JsonToken.EndArray)
        reader.Read() |> ignore
        IPEndPoint (ip, port)

    override this.WriteJson(writer: JsonWriter, state: IPEndPoint, serializer: JsonSerializer) =
        writer.WriteStartArray()
        serializer.Serialize(writer, state.Address)
        serializer.Serialize(writer, state.Port)
        writer.WriteEndArray()

module JsonMarshalling =
    let SerializerSettings: JsonSerializerSettings =
        let settings = Marshalling.DefaultSettings ()
        let ipAddressConverter = IPAddressJsonConverter()
        let ipEndPointConverter = IPEndPointJsonConverter()
        let featureBitConverter = FeatureBitJsonConverter()
        let commitmentNumberConverter = CommitmentNumberConverter()
        let commitmentPubKeyConverter = CommitmentPubKeyConverter()
        let revocationSetConverter = RevocationSetConverter()
        settings.Converters.Add ipAddressConverter
        settings.Converters.Add ipEndPointConverter
        settings.Converters.Add featureBitConverter
        settings.Converters.Add commitmentNumberConverter
        settings.Converters.Add commitmentPubKeyConverter
        settings.Converters.Add revocationSetConverter
        NBitcoin.JsonConverters.Serializer.RegisterFrontConverters settings
        settings

