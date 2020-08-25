namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Net

open DotNetLightning.Serialize
open DotNetLightning.Utils
open DotNetLightning.Crypto

open GWallet.Backend
open FSharpUtil
open FSharpUtil.UwpHacks

open NBitcoin
open Newtonsoft.Json





type UInt48 = {
    UInt64: uint64
} with
    static member private BitMask: uint64 = 0x0000ffffffffffffUL

    static member Zero: UInt48 = {
        UInt64 = 0UL
    }

    static member One: UInt48 = {
        UInt64 = 1UL
    }

    static member MaxValue: UInt48 = {
        UInt64 = UInt48.BitMask
    }

    static member FromUInt64(x: uint64): UInt48 =
        if x > UInt48.BitMask then
            raise <| ArgumentOutOfRangeException("x", "value is out of range for a UInt48")
        else
            { UInt64 = x }

    override this.ToString() =
        this.UInt64.ToString()


    static member (+) (a: UInt48, b: UInt48): UInt48 = {
        UInt64 = ((a.UInt64 <<< 8) + (b.UInt64 <<< 8)) >>> 8
    }
    
    static member (+) (a: UInt48, b: uint32): UInt48 = {
        UInt64 = ((a.UInt64 <<< 8) + ((uint64 b) <<< 8)) >>> 8
    }

    static member (-) (a: UInt48, b: UInt48): UInt48 = {
        UInt64 = ((a.UInt64 <<< 8) - (b.UInt64 <<< 8)) >>> 8
    }
    
    static member (-) (a: UInt48, b: uint32): UInt48 = {
        UInt64 = ((a.UInt64 <<< 8) - ((uint64 b) <<< 8)) >>> 8
    }

    static member (*) (a: UInt48, b: UInt48): UInt48 = {
        UInt64 = ((a.UInt64 <<< 4) * (b.UInt64 <<< 4)) >>> 8
    }

    static member (*) (a: UInt48, b: uint32): UInt48 = {
        UInt64 = ((a.UInt64 <<< 4) * ((uint64 b) <<< 4)) >>> 8
    }

    static member (&&&) (a: UInt48, b: UInt48): UInt48 = {
        UInt64 = a.UInt64 &&& b.UInt64
    }

    static member (^^^) (a: UInt48, b: UInt48): UInt48 = {
        UInt64 = a.UInt64 ^^^ b.UInt64
    }

    static member (>>>) (a: UInt48, b: int): UInt48 = {
        UInt64 = (a.UInt64 >>> b) &&& UInt48.BitMask
    }

    member this.TrailingZeros: int =
        let rec count (acc: int) (x: uint64): int =
            if acc = 48 || x &&& 1UL = 1UL then
                acc
            else
                count (acc + 1) (x >>> 1)
        count 0 this.UInt64


[<StructAttribute>]
type CommitmentNumber(index: UInt48) =
    member this.Index = index

    override this.ToString() =
        sprintf "%012x (#%i)" this.Index.UInt64 (UInt48.MaxValue - this.Index).UInt64

    static member LastCommitment: CommitmentNumber =
        CommitmentNumber UInt48.Zero

    static member FirstCommitment: CommitmentNumber =
        CommitmentNumber UInt48.MaxValue

    member this.PreviousCommitment: CommitmentNumber =
        CommitmentNumber(this.Index + UInt48.One)

    member this.NextCommitment: CommitmentNumber =
        CommitmentNumber(this.Index - UInt48.One)

    member this.Subsumes(other: CommitmentNumber): bool =
        let trailingZeros = this.Index.TrailingZeros
        (this.Index >>> trailingZeros) = (other.Index >>> trailingZeros)

    member this.PreviousUnsubsumed: Option<CommitmentNumber> =
        let trailingZeros = this.Index.TrailingZeros
        let prev = this.Index.UInt64 + (1UL <<< trailingZeros)
        if prev > UInt48.MaxValue.UInt64 then
            None
        else
            Some <| CommitmentNumber(UInt48.FromUInt64 prev)


[<StructAttribute>]
type RevocationKey(key: Key) =
    member this.Key = key

    static member BytesLength: int = Key.BytesLength

    static member FromBytes(bytes: array<byte>): RevocationKey =
        RevocationKey <| new Key(bytes)

    member this.ToByteArray(): array<byte> =
        this.Key.ToBytes()

    member this.DeriveChild (thisCommitmentNumber: CommitmentNumber)
                            (childCommitmentNumber: CommitmentNumber)
                                : Option<RevocationKey> =
        if thisCommitmentNumber.Subsumes childCommitmentNumber then
            let commonBits = thisCommitmentNumber.Index.TrailingZeros
            let index = childCommitmentNumber.Index
            let mutable secret = this.ToByteArray()
            for bit in (commonBits - 1) .. -1 .. 0 do
                if (index >>> bit) &&& UInt48.One = UInt48.One then
                    let byteIndex = bit / 8
                    let bitIndex = bit % 8
                    secret.[byteIndex] <- secret.[byteIndex] ^^^ (1uy <<< bitIndex)
                    secret <- NBitcoin.Crypto.Hashes.SHA256 secret
            Some <| RevocationKey(new Key(secret))
        else
            None

    member this.CommitmentPubKey: CommitmentPubKey =
        CommitmentPubKey this.Key.PubKey

type InsertRevocationKeyError =
    | UnexpectedCommitmentNumber of CommitmentNumber * CommitmentNumber
    | KeyMismatch of previousCommitmentNumber: CommitmentNumber * CommitmentNumber


type RevocationSet private (keys: list<CommitmentNumber * RevocationKey>) =
    new() = RevocationSet(List.empty)

    member this.Keys = keys

    static member FromKeys (keys: list<CommitmentNumber * RevocationKey>): RevocationSet =
        let rec sanityCheck (commitmentNumbers: list<CommitmentNumber>): bool =
            if commitmentNumbers.IsEmpty then
                true
            else
                let commitmentNumber = commitmentNumbers.Head
                let tail = commitmentNumbers.Tail
                match commitmentNumber.PreviousUnsubsumed with
                | None -> tail.IsEmpty
                | Some expectedCommitmentNumber ->
                    if tail.IsEmpty then
                        false
                    else
                        let nextCommitmentNumber = tail.Head
                        if nextCommitmentNumber <> expectedCommitmentNumber then
                            false
                        else
                            sanityCheck tail
        let commitmentNumbers, _ = List.unzip keys
        if not (sanityCheck commitmentNumbers) then
            failwith <| SPrintF1 "commitment number list is malformed: %A" commitmentNumbers
        RevocationSet keys

    member this.NextCommitmentNumber: CommitmentNumber =
        if this.Keys.IsEmpty then
            CommitmentNumber.FirstCommitment
        else
            let prevCommitmentNumber, _ = this.Keys.Head
            prevCommitmentNumber.NextCommitment

    member this.InsertRevocationKey (commitmentNumber: CommitmentNumber)
                                    (revocationKey: RevocationKey)
                                        : Result<RevocationSet, InsertRevocationKeyError> =
        let storedCommitmentNumber, storedRevocationKey = this.Keys.Head
        match revocationKey.DeriveChild commitmentNumber storedCommitmentNumber with
        | Some derivedRevocationKey ->
            if derivedRevocationKey <> storedRevocationKey then
                Error <| KeyMismatch (storedCommitmentNumber, commitmentNumber)
            else
                failwith "meh"
        | None ->
            let res = (commitmentNumber, revocationKey) :: keys
            Ok <| RevocationSet res

    member this.GetRevocationKey (commitmentNumber: CommitmentNumber)
                                     : Option<RevocationKey> =
        let rec fold (keys: list<CommitmentNumber * RevocationKey>) =
            if keys.IsEmpty then
                None
            else
                let storedCommitmentNumber, storedRevocationKey = keys.Head
                match storedRevocationKey.DeriveChild storedCommitmentNumber commitmentNumber with
                | Some revocationKey -> Some revocationKey
                | None -> fold keys.Tail
        fold this.Keys

    member this.LastRevocationKey(): Option<RevocationKey> =
        if this.Keys.IsEmpty then
            None
        else
            let _, revocationKey = this.Keys.Head
            Some revocationKey


type [<StructAttribute>] CommitmentPubKey(pubKey: PubKey) =
        member this.PubKey = pubKey

        static member BytesLength: int =
            failwith "tmp:NIE"

        static member FromBytes(bytes: array<byte>): CommitmentPubKey =
            CommitmentPubKey <| PubKey bytes

        member this.ToByteArray(): array<byte> =
            this.PubKey.ToBytes()

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
            |> GChannelId
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
            assert (reader.TokenType = JsonToken.StartArray)
            reader.Read() |> ignore
            let ip = serializer.Deserialize<IPAddress> reader
            reader.Read() |> ignore
            let port = serializer.Deserialize<int32> reader
            assert (reader.TokenType = JsonToken.EndArray)
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

