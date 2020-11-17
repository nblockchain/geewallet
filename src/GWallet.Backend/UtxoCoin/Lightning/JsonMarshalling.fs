namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Net

open DotNetLightning.Serialization
open DotNetLightning.Utils
open DotNetLightning.Crypto

open GWallet.Backend
open FSharpUtil

open NBitcoin
open Newtonsoft.Json

module JsonMarshalling =
    type internal PerCommitmentSecretConverter() =
        inherit JsonConverter<PerCommitmentSecret>()

        override this.ReadJson(reader: JsonReader, _: Type, _: PerCommitmentSecret, _: bool, serializer: JsonSerializer) =
            let serializedPerCommitmentSecret = serializer.Deserialize<string> reader
            let hex = NBitcoin.DataEncoders.HexEncoder()
            let bytes = hex.DecodeData serializedPerCommitmentSecret
            let key = new Key(bytes)
            PerCommitmentSecret key

        override this.WriteJson(writer: JsonWriter, state: PerCommitmentSecret, serializer: JsonSerializer) =
            let serializedPerCommitmentSecret: string = state.RawKey().ToHex()
            serializer.Serialize(writer, serializedPerCommitmentSecret)

    type internal PerCommitmentPointConverter() =
        inherit JsonConverter<PerCommitmentPoint>()

        override this.ReadJson(reader: JsonReader, _: Type, _: PerCommitmentPoint, _: bool, serializer: JsonSerializer) =
            let serializedPerCommitmentPoint = serializer.Deserialize<string> reader
            let hex = NBitcoin.DataEncoders.HexEncoder()
            serializedPerCommitmentPoint |> hex.DecodeData |> PubKey |> PerCommitmentPoint

        override this.WriteJson(writer: JsonWriter, state: PerCommitmentPoint, serializer: JsonSerializer) =
            let serializedPerCommitmentPoint: string = state.RawPubKey().ToHex()
            serializer.Serialize(writer, serializedPerCommitmentPoint)

    type internal FundingPubKeyConverter() =
        inherit JsonConverter<FundingPubKey>()

        override this.ReadJson(reader: JsonReader, _: Type, _: FundingPubKey, _: bool, serializer: JsonSerializer) =
            let serializedFundingPubKey = serializer.Deserialize<string> reader
            let hex = NBitcoin.DataEncoders.HexEncoder()
            serializedFundingPubKey |> hex.DecodeData |> PubKey |> FundingPubKey

        override this.WriteJson(writer: JsonWriter, state: FundingPubKey, serializer: JsonSerializer) =
            let serializedFundingPubKey: string = state.RawPubKey().ToHex()
            serializer.Serialize(writer, serializedFundingPubKey)

    type internal RevocationBasepointConverter() =
        inherit JsonConverter<RevocationBasepoint>()

        override this.ReadJson(reader: JsonReader, _: Type, _: RevocationBasepoint, _: bool, serializer: JsonSerializer) =
            let serializedRevocationBasepoint = serializer.Deserialize<string> reader
            let hex = NBitcoin.DataEncoders.HexEncoder()
            serializedRevocationBasepoint |> hex.DecodeData |> PubKey |> RevocationBasepoint

        override this.WriteJson(writer: JsonWriter, state: RevocationBasepoint, serializer: JsonSerializer) =
            let serializedRevocationBasepoint: string = state.RawPubKey().ToHex()
            serializer.Serialize(writer, serializedRevocationBasepoint)

    type internal PaymentBasepointConverter() =
        inherit JsonConverter<PaymentBasepoint>()

        override this.ReadJson(reader: JsonReader, _: Type, _: PaymentBasepoint, _: bool, serializer: JsonSerializer) =
            let serializedPaymentBasepoint = serializer.Deserialize<string> reader
            let hex = NBitcoin.DataEncoders.HexEncoder()
            serializedPaymentBasepoint |> hex.DecodeData |> PubKey |> PaymentBasepoint

        override this.WriteJson(writer: JsonWriter, state: PaymentBasepoint, serializer: JsonSerializer) =
            let serializedPaymentBasepoint: string = state.RawPubKey().ToHex()
            serializer.Serialize(writer, serializedPaymentBasepoint)

    type internal DelayedPaymentBasepointConverter() =
        inherit JsonConverter<DelayedPaymentBasepoint>()

        override this.ReadJson(reader: JsonReader, _: Type, _: DelayedPaymentBasepoint, _: bool, serializer: JsonSerializer) =
            let serializedDelayedPaymentBasepoint = serializer.Deserialize<string> reader
            let hex = NBitcoin.DataEncoders.HexEncoder()
            serializedDelayedPaymentBasepoint |> hex.DecodeData |> PubKey |> DelayedPaymentBasepoint

        override this.WriteJson(writer: JsonWriter, state: DelayedPaymentBasepoint, serializer: JsonSerializer) =
            let serializedDelayedPaymentBasepoint: string = state.RawPubKey().ToHex()
            serializer.Serialize(writer, serializedDelayedPaymentBasepoint)

    type internal HtlcBasepointConverter() =
        inherit JsonConverter<HtlcBasepoint>()

        override this.ReadJson(reader: JsonReader, _: Type, _: HtlcBasepoint, _: bool, serializer: JsonSerializer) =
            let serializedHtlcBasepoint = serializer.Deserialize<string> reader
            let hex = NBitcoin.DataEncoders.HexEncoder()
            serializedHtlcBasepoint |> hex.DecodeData |> PubKey |> HtlcBasepoint

        override this.WriteJson(writer: JsonWriter, state: HtlcBasepoint, serializer: JsonSerializer) =
            let serializedHtlcBasepoint: string = state.RawPubKey().ToHex()
            serializer.Serialize(writer, serializedHtlcBasepoint)

    type internal CommitmentNumberConverter() =
        inherit JsonConverter<CommitmentNumber>()

        override this.ReadJson(reader: JsonReader, _: Type, _: CommitmentNumber, _: bool, serializer: JsonSerializer) =
            let serializedCommitmentNumber = serializer.Deserialize<uint64> reader
            CommitmentNumber <| (UInt48.MaxValue - UInt48.FromUInt64 serializedCommitmentNumber)

        override this.WriteJson(writer: JsonWriter, state: CommitmentNumber, serializer: JsonSerializer) =
            let serializedCommitmentNumber: uint64 = (UInt48.MaxValue - state.Index()).UInt64
            serializer.Serialize(writer, serializedCommitmentNumber)

    type internal PerCommitmentSecretStoreConverter() =
        inherit JsonConverter<PerCommitmentSecretStore>()

        override this.ReadJson(reader: JsonReader, _: Type, _: PerCommitmentSecretStore, _: bool, serializer: JsonSerializer) =
            let keys = serializer.Deserialize<list<CommitmentNumber * PerCommitmentSecret>> reader
            PerCommitmentSecretStore.FromSecrets keys

        override this.WriteJson(writer: JsonWriter, state: PerCommitmentSecretStore, serializer: JsonSerializer) =
            let keys: list<CommitmentNumber * PerCommitmentSecret> = state.Secrets
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

    type internal FeatureBitsJsonConverter() =
        inherit JsonConverter<FeatureBits>()

        override self.ReadJson(reader: JsonReader, _: Type, _: FeatureBits, _: bool, serializer: JsonSerializer) =
            let serializedFeatureBits = serializer.Deserialize<string> reader
            UnwrapResult (FeatureBits.TryParse serializedFeatureBits) "error decoding feature bit"

        override self.WriteJson(writer: JsonWriter, state: FeatureBits, serializer: JsonSerializer) =
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
        let featureBitConverter = FeatureBitsJsonConverter()
        let channelIdentifierConverter = ChannelIdentifierConverter()
        let commitmentNumberConverter = CommitmentNumberConverter()
        //let commitmentPubKeyConverter = CommitmentPubKeyConverter()
        let perCommitmentSecretConverter = PerCommitmentSecretConverter()
        let perCommitmentPointConverter = PerCommitmentPointConverter()
        let fundingPubKeyConverter = FundingPubKeyConverter()
        let revocationBasepointConverter = RevocationBasepointConverter()
        let paymentBasepointConverter = PaymentBasepointConverter()
        let delayedPaymentBasepointConverter = DelayedPaymentBasepointConverter()
        let htlcBasepointConverter = HtlcBasepointConverter()
        let perCommitmentSecretStoreConverter = PerCommitmentSecretStoreConverter()
        settings.Converters.Add ipAddressConverter
        settings.Converters.Add ipEndPointConverter
        settings.Converters.Add featureBitConverter
        settings.Converters.Add channelIdentifierConverter
        settings.Converters.Add commitmentNumberConverter
        //settings.Converters.Add commitmentPubKeyConverter
        settings.Converters.Add perCommitmentSecretConverter
        settings.Converters.Add perCommitmentPointConverter
        settings.Converters.Add fundingPubKeyConverter
        settings.Converters.Add revocationBasepointConverter
        settings.Converters.Add paymentBasepointConverter
        settings.Converters.Add delayedPaymentBasepointConverter
        settings.Converters.Add htlcBasepointConverter
        settings.Converters.Add perCommitmentSecretStoreConverter
        NBitcoin.JsonConverters.Serializer.RegisterFrontConverters settings
        settings

