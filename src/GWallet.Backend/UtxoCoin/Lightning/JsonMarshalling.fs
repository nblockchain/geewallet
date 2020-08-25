namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Net
open System.Collections

open GWallet.Backend
open FSharpUtil
open FSharpUtil.UwpHacks

open NBitcoin
open Newtonsoft.Json


module BclEx =

    let ResOfChoice c =
      match c with
      | Choice1Of2 x -> Ok x
      | Choice2Of2 x -> Error x

    let FlipBit (a: System.Byte) =
        ((a &&& 0x1uy)  <<< 7) ||| ((a &&& 0x2uy)  <<< 5) |||
        ((a &&& 0x4uy)  <<< 3) ||| ((a &&& 0x8uy)  <<< 1) |||
        ((a &&& 0x10uy) >>> 1) ||| ((a &&& 0x20uy) >>> 3) |||
        ((a &&& 0x40uy) >>> 5) ||| ((a &&& 0x80uy) >>> 7)

    let PrintBits (ba: BitArray) =
        let sb = System.Text.StringBuilder()
        for b in ba do
            (if b then "1" else "0") |> sb.Append |> ignore
        sb.ToString()

    let FromBytes(ba: byte[]) =
        ba |> Array.map(fun b -> FlipBit b) |> BitArray

    let Reverse this =
        let boolArray: array<bool> = Array.ofSeq (Seq.cast this)
        Array.Reverse boolArray
        BitArray(boolArray)

    let ToByteArray (ba: BitArray): byte[] =
        if ba.Length = 0 then
            [||]
        else

            let leadingZeros =
                match (Seq.tryFindIndex (fun b -> b) (Seq.cast ba)) with
                | Some i -> i
                | None -> ba.Length
            let trueLength = ba.Length - leadingZeros
            let desiredLength = ((trueLength + 7) / 8) * 8
            let difference = desiredLength - ba.Length
            let bitArray =
                if difference < 0 then
                    // Drop zeroes from the front of the array until we have a multiple of 8 bits
                    let shortenedBitArray = BitArray(desiredLength)
                    for i in 0 .. (desiredLength - 1) do
                        shortenedBitArray.[i] <- ba.[i - difference]
                    shortenedBitArray
                else if difference > 0 then
                    // Push zeroes to the front of the array until we have a multiple of 8 bits
                    let lengthenedBitArray = BitArray(desiredLength)
                    for i in 0 .. (ba.Length - 1) do
                        lengthenedBitArray.[i + difference] <- ba.[i]
                    lengthenedBitArray
                else
                    ba
            // Copy the bit array to a byte array, then flip the bytes.
            let byteArray: byte[] = Array.zeroCreate(desiredLength / 8)
            bitArray.CopyTo(byteArray, 0)
            byteArray |> Array.map (fun b -> FlipBit b)


type FeaturesSupport =
    | Mandatory
    | Optional

/// Feature bits specified in BOLT 9.
/// It has no constructors, use its static members to instantiate
type Feature = private {
    RfcName: string
    Mandatory: int
}
    with
    member this.MandatoryBitPosition = this.Mandatory
    member this.OptionalBitPosition = this.Mandatory + 1
    member this.BitPosition(support: FeaturesSupport) =
        match support with
        | Mandatory -> this.MandatoryBitPosition
        | Optional -> this.OptionalBitPosition

    override this.ToString() = this.RfcName

    static member OptionDataLossProtect = {
        RfcName = "option_data_loss_protect"
        Mandatory = 0
    }
        
    static member InitialRoutingSync = {
        RfcName = "initial_routing_sync"
        Mandatory = 2
    }
    
    static member OptionUpfrontShutdownScript = {
        RfcName = "option_upfront_shutdown_script"
        Mandatory = 4
    }
    
    static member ChannelRangeQueries = {
        RfcName = "gossip_queries"
        Mandatory = 6
    }
    
    static member VariableLengthOnion = {
        RfcName = "var_onion_optin"
        Mandatory = 8
    }

    static member ChannelRangeQueriesExtended = {
        RfcName = "gossip_queries_ex"
        Mandatory = 10
    }
    
    static member OptionStaticRemoteKey = {
        RfcName = "option_static_remotekey"
        Mandatory = 12
    }
    
    static member PaymentSecret = {
        RfcName = "payment_secret"
        Mandatory = 14
    }
    
    static member BasicMultiPartPayment = {
        RfcName = "basic_mpp"
        Mandatory = 16
    }

type FeatureError =
    | UnknownRequiredFeature of string
    | BogusFeatureDependency of string
    member this.Message() =
        match this with
        | UnknownRequiredFeature msg
        | BogusFeatureDependency msg -> msg

type FeatureBit (bitArray: BitArray) =
    member val BitArray = bitArray
    member this.ByteArray
        with get() =
            BclEx.ToByteArray bitArray

    override this.ToString() =
        BclEx.PrintBits bitArray

    // --- equality and comparison members ----
    member this.Equals(o: FeatureBit) =
        this.ByteArray = o.ByteArray

    interface IEquatable<FeatureBit> with
        member this.Equals(o: FeatureBit) = this.Equals(o)
    override this.Equals(other: obj) =
        match other with
        | :? FeatureBit as o -> this.Equals(o)
        | _ -> false
        
    override this.GetHashCode() =
        let mutable num = 0
        for i in bitArray do
            num <- -1640531527 + i.GetHashCode() + ((num <<< 6) + (num >>> 2))
        num

    static member TryParse(str: string): Result<FeatureBit,string> =
        let tryParse(str: string): Result<BitArray,string> =
            let mutable str = str.Trim().Clone() :?> string
            if str.StartsWith("0b", StringComparison.OrdinalIgnoreCase) then
                str <- str.Substring("0b".Length)
            let array = Array.zeroCreate(str.Length)
            let mutable hasFunnyChar = -1
            for i in 0..str.Length - 1 do
                if hasFunnyChar <> -1 then
                    ()
                else
                    if str.[i] = '0' then
                        array.[i] <- false
                    else
                        if str.[i] = '1' then
                            array.[i] <- true
                        else
                            hasFunnyChar <- i
            if hasFunnyChar <> -1 then
                sprintf "Failed to parse BitArray! it must have only '0' or '1' but we found %A" str.[hasFunnyChar]
                |> Error
            else
                BitArray(array) |> Ok
        let ba = tryParse str
        match ba with
        | Error x -> Error x
        | Ok v ->
            let fb = Ok <| FeatureBit v
            match fb with
            | Error fe -> Error <| fe.ToString()
            | Ok vv -> Ok vv

    member this.CompareTo(o: FeatureBit) =
        if bitArray.Length > o.BitArray.Length then
            -1
        else
            if bitArray.Length < o.BitArray.Length then
                1
            else
                let mutable result = 0
                for i in 0..this.BitArray.Length - 1 do
                    if bitArray.[i] > o.BitArray.[i] then
                        result <- -1
                    elif bitArray.[i] < o.BitArray.[i] then
                        result <- 1
                result

    interface IComparable with
        member this.CompareTo(o) =
            match o with
            | :? FeatureBit as fb -> this.CompareTo(fb)
            | _ -> -1
    // --------


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

    member this.PreviousUnsubsumed: Option<CommitmentNumber> =
        let trailingZeros = this.Index.TrailingZeros
        let prev = this.Index.UInt64 + (1UL <<< trailingZeros)
        if prev > UInt48.MaxValue.UInt64 then
            None
        else
            Some <| CommitmentNumber(UInt48.FromUInt64 prev)



type [<StructAttribute>] CommitmentPubKey(pubKey: PubKey) =
        member this.PubKey = pubKey


[<StructAttribute>]
type RevocationKey(key: Key) =
    member this.Key = key

    member this.ToByteArray(): array<byte> =
        this.Key.ToBytes()

    member this.CommitmentPubKey: CommitmentPubKey =
        CommitmentPubKey this.Key.PubKey


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

