namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Net

open Newtonsoft.Json
open NBitcoin
open DotNetLightning.Channel
open DotNetLightning.Utils
open DotNetLightning.Crypto
open DotNetLightning.Transactions

open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks

type SerializedCommitmentSpec =
    {
        HTLCs: Map<HTLCId, DirectedHTLC>
        FeeRatePerKw: FeeRatePerKw
        ToLocal: LNMoney
        ToRemote: LNMoney
    }

type SerializedCommitments =
    {
        ChannelId: ChannelIdentifier
        ChannelFlags: uint8
        FundingScriptCoin: ScriptCoin
        LocalChanges: LocalChanges
        LocalCommit: LocalCommit
        LocalNextHTLCId: HTLCId
        LocalParams: LocalParams
        OriginChannels: Map<HTLCId, HTLCSource>
        RemoteChanges: RemoteChanges
        RemoteCommit: RemoteCommit
        RemoteNextCommitInfo: RemoteNextCommitInfo
        RemoteNextHTLCId: HTLCId
        RemoteParams: RemoteParams
        RemotePerCommitmentSecrets: RevocationSet
    }

type private CommitmentsJsonConverter() =
    inherit JsonConverter<Commitments>()

    override __.ReadJson(reader: JsonReader, _: Type, _: Commitments, _: bool, serializer: JsonSerializer) =
        let serializedCommitments = serializer.Deserialize<SerializedCommitments> reader
        let commitments: Commitments = {
            RemotePerCommitmentSecrets = serializedCommitments.RemotePerCommitmentSecrets
            RemoteParams = serializedCommitments.RemoteParams
            RemoteNextHTLCId = serializedCommitments.RemoteNextHTLCId
            RemoteNextCommitInfo = serializedCommitments.RemoteNextCommitInfo
            RemoteCommit = serializedCommitments.RemoteCommit
            RemoteChanges = serializedCommitments.RemoteChanges
            OriginChannels = serializedCommitments.OriginChannels
            LocalParams = serializedCommitments.LocalParams
            LocalNextHTLCId = serializedCommitments.LocalNextHTLCId
            LocalCommit = serializedCommitments.LocalCommit
            LocalChanges = serializedCommitments.LocalChanges
            FundingScriptCoin = serializedCommitments.FundingScriptCoin
            ChannelId = serializedCommitments.ChannelId.DnlChannelId
            ChannelFlags = serializedCommitments.ChannelFlags
        }
        commitments

    override __.WriteJson(writer: JsonWriter, state: Commitments, serializer: JsonSerializer) =
        serializer.Serialize(writer, {
            ChannelId = ChannelIdentifier.FromDnl state.ChannelId
            ChannelFlags = state.ChannelFlags
            FundingScriptCoin = state.FundingScriptCoin
            LocalChanges = state.LocalChanges
            LocalCommit = state.LocalCommit
            LocalNextHTLCId = state.LocalNextHTLCId
            LocalParams = state.LocalParams
            OriginChannels = state.OriginChannels
            RemoteChanges = state.RemoteChanges
            RemoteCommit = state.RemoteCommit
            RemoteNextCommitInfo = state.RemoteNextCommitInfo
            RemoteNextHTLCId = state.RemoteNextHTLCId
            RemoteParams = state.RemoteParams
            RemotePerCommitmentSecrets = state.RemotePerCommitmentSecrets
        })

type GTxIndexInBlock = | TxIndexInBlock of uint32 with
    member x.Value = let (TxIndexInBlock v) = x in v

type GShortChannelId = {
    BlockIndex: GTxIndexInBlock
}

[<StructuralComparison;StructuralEquality;CLIMutable>]
type GUnsignedChannelAnnouncementMsg = {
    mutable Features: DotNetLightning.Serialize.FeatureBit
    mutable ChainHash: uint256
    mutable ShortChannelId: ShortChannelId
    mutable NodeId1: NodeId
    mutable NodeId2: NodeId
    mutable BitcoinKey1: ComparablePubKey
    mutable BitcoinKey2: ComparablePubKey
    mutable ExcessData: byte[]
}

//[<Struct>]
type GUInt48 = {
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
        UInt64 = GUInt48.BitMask
    }

    static member FromUInt64(x: uint64): UInt48 =
        if x > GUInt48.BitMask then
            raise <| ArgumentOutOfRangeException("x", "value is out of range for a UInt48")
        else
            { UInt64 = x }

    override this.ToString() =
        this.UInt64.ToString()

    //member this.GetBytesBigEndian(): array<byte> =
    //    this.UInt64.GetBytesBigEndian().[2..]

    //static member FromBytesBigEndian(bytes6: array<byte>) =
    //    if bytes6.Length <> 6 then
    //        failwith "UInt48.FromBytesBigEndian expects a 6 byte array"
    //    else
    //        let bytes8 = Array.concat [| [| 0uy; 0uy |]; bytes6 |]
    //        { UInt64 = System.UInt64.FromBytesBigEndian bytes8 }

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
        UInt64 = (a.UInt64 >>> b) &&& GUInt48.BitMask
    }

    member this.TrailingZeros: int =
        let rec count (acc: int) (x: uint64): int =
            if acc = 48 || x &&& 1UL = 1UL then
                acc
            else
                count (acc + 1) (x >>> 1)
        count 0 this.UInt64

[<StructuralComparison;StructuralEquality>]
type GTxId = | GTxId of uint256 with
    member x.Value = let (GTxId v) = x in v
    static member Zero = uint256.Zero |> TxId

type GNormalData =   {
                        ShortChannelId: GShortChannelId;
                        Buried: bool;
                    }

type GChannelState =
    /// normal
    | Normal of GNormalData

    /// Closing
    | Closing

type SerializedChannel =
    {
        ChannelIndex: int
        //Network: Network
        ChanState: GChannelState
        //AccountFileName: string
        // FIXME: should store just RemoteNodeEndPoint instead of CounterpartyIP+RemoteNodeId?
        //CounterpartyIP: IPEndPoint
        //RemoteNodeId: NodeId
        // this is the amount of confirmations that the counterparty told us that the funding transaction needs
        //MinSafeDepth: BlockHeightOffset32
    }
    static member LightningSerializerSettings: JsonSerializerSettings =
        let settings = JsonMarshalling.SerializerSettings
        let commitmentsConverter = CommitmentsJsonConverter()
        settings.Converters.Add commitmentsConverter
        settings


module ChannelSerialization =
    let internal Commitments (_: SerializedChannel): Commitments =
        failwith "TMP:NIE"
        //UnwrapOption
        //    serializedChannel.ChanState.Commitments
        //    "A SerializedChannel is only created once a channel has started \
        //    being established and must therefore have an initial commitment"

    let IsFunder (serializedChannel: SerializedChannel): bool =
        (Commitments serializedChannel).LocalParams.IsFunder

    let internal Capacity (serializedChannel: SerializedChannel): Money =
        (Commitments serializedChannel).FundingScriptCoin.Amount

    let internal Balance (serializedChannel: SerializedChannel): DotNetLightning.Utils.LNMoney =
        (Commitments serializedChannel).LocalCommit.Spec.ToLocal

    let internal SpendableBalance (serializedChannel: SerializedChannel): DotNetLightning.Utils.LNMoney =
        (Commitments serializedChannel).SpendableBalance()

    // How low the balance can go. A channel must maintain enough balance to
    // cover the channel reserve. The funder must also keep enough in the
    // channel to cover the closing fee.
    let internal MinBalance (serializedChannel: SerializedChannel): DotNetLightning.Utils.LNMoney =
        (Balance serializedChannel) - (SpendableBalance serializedChannel)

    // How high the balance can go. The fundee will only be able to receive up
    // to this amount before the funder no longer has enough funds to cover
    // the channel reserve and closing fee.
    let internal MaxBalance (serializedChannel: SerializedChannel): DotNetLightning.Utils.LNMoney =
        let capacity = LNMoney.FromMoney <| (Capacity serializedChannel)
        let channelReserve =
            LNMoney.FromMoney (Commitments serializedChannel).LocalParams.ChannelReserveSatoshis
        let fee =
            if (IsFunder serializedChannel) then
                let feeRate = (Commitments serializedChannel).LocalCommit.Spec.FeeRatePerKw
                let weight = COMMITMENT_TX_BASE_WEIGHT
                LNMoney.FromMoney <| feeRate.CalculateFeeFromWeight weight
            else
                LNMoney.Zero
        capacity - channelReserve - fee

    let ChannelId (serializedChannel: SerializedChannel): ChannelIdentifier =
        ChannelIdentifier.FromDnl (Commitments serializedChannel).ChannelId

