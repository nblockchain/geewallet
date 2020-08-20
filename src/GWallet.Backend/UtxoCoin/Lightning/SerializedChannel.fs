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

[<Struct;StructuredFormatDisplay("{AsString}")>]
type GShortChannelId = {
    BlockHeight: DotNetLightning.Utils.Primitives.BlockHeight
    BlockIndex: DotNetLightning.Utils.Primitives.TxIndexInBlock
    TxOutIndex: DotNetLightning.Utils.Primitives.TxOutIndex
}
    with

    static member From8Bytes(b: byte[]): ShortChannelId =
        let bh = NBitcoin.Utils.ToUInt32 (Array.concat [| [| 0uy |]; b.[0..2]; |], false)
        let bi = NBitcoin.Utils.ToUInt32 (Array.concat [| [| 0uy |]; b.[3..5]; |], false)
        let txOutIndex = NBitcoin.Utils.ToUInt16(b.[6..7], false)

        {
            BlockHeight = bh |> DotNetLightning.Utils.Primitives.BlockHeight
            BlockIndex = bi |> DotNetLightning.Utils.Primitives.TxIndexInBlock
            TxOutIndex = txOutIndex |> DotNetLightning.Utils.Primitives.TxOutIndex
        }

    static member FromUInt64(rawData: uint64) =
        NBitcoin.Utils.ToBytes(rawData, false) |> ShortChannelId.From8Bytes
        
    member this.ToBytes(): byte [] =
        Array.concat [|
                    NBitcoin.Utils.ToBytes(this.BlockHeight.Value, false).[1..3]
                    NBitcoin.Utils.ToBytes(this.BlockIndex.Value, false).[1..3]
                    NBitcoin.Utils.ToBytes(this.TxOutIndex.Value, false)
                |]
    override this.ToString() =
        SPrintF3 "%dx%dx%d" this.BlockHeight.Value this.BlockIndex.Value this.TxOutIndex.Value

    member this.AsString = this.ToString()
        
    static member TryParse(s: string) =
        let items = s.Split('x')
        let err = Error (SPrintF1 "Failed to parse %s" s)
        if (items.Length <> 3)  then err else
        match (items.[0] |> UInt32.TryParse), (items.[1] |> UInt32.TryParse), (items.[2] |> UInt16.TryParse) with
        | (true, h), (true, blockI), (true, outputI) ->
            {
                BlockHeight = h |> DotNetLightning.Utils.Primitives.BlockHeight
                BlockIndex = blockI |> DotNetLightning.Utils.Primitives.TxIndexInBlock
                TxOutIndex = outputI |> DotNetLightning.Utils.Primitives.TxOutIndex
            } |> Ok
        | _ -> err
    //static member ParseUnsafe(s: string) =
    //    ShortChannelId.TryParse s
    //    |> Result.defaultWith (fun _ -> raise <| FormatException(SPrintF1 "Failed to parse %s" s))

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

