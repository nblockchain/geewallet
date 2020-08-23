namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Net

open Newtonsoft.Json
open NBitcoin

open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks

[<Flags>]
type LNMoneyUnit =
    | BTC = 100000000000UL
    | MilliBTC = 100000000UL
    | Bit = 100000UL
    | Micro = 100000UL
    | Satoshi = 1000UL
    | Nano = 100UL
    | MilliSatoshi = 1UL
    | Pico = 1UL

type LNMoney = | LNMoney of int64 with

    // --- constructors -----
    static member private CheckMoneyUnit(v: LNMoneyUnit, paramName: string) =
        let typeOfMoneyUnit = typeof<LNMoneyUnit>
        if not (Enum.IsDefined(typeOfMoneyUnit, v)) then
            raise (ArgumentException(SPrintF1 "Invalid value for MoneyUnit %s" paramName))

    static member private FromUnit(amount: decimal, lnUnit: LNMoneyUnit) =
        LNMoney.CheckMoneyUnit(lnUnit, "unit") |> ignore
        let satoshi = Checked.op_Multiply (amount) (decimal lnUnit)
        LNMoney(Checked.int64 satoshi)

    static member FromMoney (money: Money) =
        LNMoney.Satoshis money.Satoshi

    static member Coins(coins: decimal) =
        LNMoney.FromUnit(coins * (decimal LNMoneyUnit.BTC), LNMoneyUnit.MilliSatoshi)

    static member Satoshis(satoshis: decimal) =
        LNMoney.FromUnit(satoshis * (decimal LNMoneyUnit.Satoshi), LNMoneyUnit.MilliSatoshi)

    static member Satoshis(sats: int64) =
        LNMoney.MilliSatoshis(Checked.op_Multiply 1000L sats)

    static member inline Satoshis(sats) =
        LNMoney.Satoshis(int64 sats)

    static member Satoshis(sats: uint64) =
        LNMoney.MilliSatoshis(Checked.op_Multiply 1000UL sats)

    static member MilliSatoshis(sats: int64) =
        LNMoney(sats)

    static member inline MilliSatoshis(sats) =
        LNMoney(int64 sats)

    static member MilliSatoshis(sats: uint64) =
        LNMoney(Checked.int64 sats)

    static member Zero = LNMoney(0L)
    static member One = LNMoney(1L)


    // -------- Arithmetic operations
    static member (+) (LNMoney a, LNMoney b) = LNMoney(a + b)
    static member (-) (LNMoney a, LNMoney b) = LNMoney(a - b)
    static member (*) (LNMoney a, LNMoney b) = LNMoney(a * b)
    static member (/) (LNMoney a, LNMoney b) = LNMoney(a / b)
    static member inline (/) (LNMoney a, b) = LNMoney(a / (int64 b))
    static member inline (+) (LNMoney a, b) = LNMoney(a + (int64 b))
    static member inline (-) (LNMoney a, b) = LNMoney(a - (int64 b))
    static member inline (*) (LNMoney a, b) = LNMoney(a * (int64 b))
    static member Max(LNMoney a, LNMoney b) = if a >= b then LNMoney a else LNMoney b
    static member Min(LNMoney a, LNMoney b) = if a <= b then LNMoney a else LNMoney b
    
    static member MaxValue =
        let maxSatoshis = 21000000UL * (uint64 Money.COIN)
        LNMoney.Satoshis maxSatoshis

    static member op_Implicit (money: Money) = LNMoney.Satoshis(money.Satoshi)

    // --------- Utilities
    member this.Abs() =
        if this < LNMoney.Zero then LNMoney(-this.Value) else this

    member this.MilliSatoshi = let (LNMoney v) = this in v
    member this.Satoshi = this.MilliSatoshi / 1000L
    member this.BTC = this.MilliSatoshi / (int64 LNMoneyUnit.BTC)
    member this.Value = this.MilliSatoshi
    member this.ToMoney() = this.Satoshi |> Money

    member this.Split(parts: int): LNMoney seq =
        if parts <= 0 then
            raise (ArgumentOutOfRangeException("parts"))
        else
            let mutable remain = 0L
            let res = Math.DivRem(this.MilliSatoshi, int64 parts, &remain)
            seq {
                for _ in 0..(parts - 1) do
                    yield LNMoney.Satoshis (decimal (res + (if remain > 0L then 1L else 0L)))
                    remain <- remain - 1L
            }

type HTLCId = | HTLCId of uint64 with
    static member Zero = HTLCId(0UL)
    member x.Value = let (HTLCId v) = x in v

    static member (+) (a: HTLCId, b: uint64) = (a.Value + b) |> HTLCId

type internal Direction =
    | In
    | Out
    with
        member this.Opposite =
            match this with
            | In -> Out
            | Out -> In

type DirectedHTLC = internal {
    Direction: Direction
    Add: DotNetLightning.Serialize.Msgs.UpdateAddHTLCMsg
}

type FeeRatePerKw = | FeeRatePerKw of uint32 with
        member x.Value = let (FeeRatePerKw v) = x in v
        static member FromFee(fee: Money, weight: uint64) =
            (((uint64 fee.Satoshi) * weight) / 1000UL)
            |> uint32
            |> FeeRatePerKw
            
        static member FromFeeAndVSize(fee: Money, vsize: uint64) =
            FeeRatePerKw.FromFee(fee, vsize * 4UL)

        member this.CalculateFeeFromWeight(weight) =
            Money.Satoshis(uint64 this.Value * weight / 1000UL)
            
        member this.CalculateFeeFromVirtualSize(vSize) =
            this.CalculateFeeFromWeight (vSize * 4UL)
        member this.CalculateFeeFromVirtualSize(tx: Transaction) =
            for i in tx.Inputs do
                if isNull i.WitScript || i.WitScript = WitScript.Empty then
                    invalidArg "tx" "Should never hold non-segwit input."
            this.CalculateFeeFromVirtualSize(uint64 (tx.GetVirtualSize()))
            
        member this.AsNBitcoinFeeRate() =
            this.Value |> uint64 |> (*)4UL |> Money.Satoshis |> FeeRate

        static member Max(a: FeeRatePerKw, b: FeeRatePerKw) =
            if (a.Value >= b.Value) then a else b
        static member (+) (a: FeeRatePerKw, b: uint32) =
            (a.Value + b) |> FeeRatePerKw
        static member (*) (a: FeeRatePerKw, b: uint32) =
            (a.Value * b) |> FeeRatePerKw


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
        LocalChanges: DotNetLightning.Channel.LocalChanges
        LocalCommit: DotNetLightning.Channel.LocalCommit
        LocalNextHTLCId: DotNetLightning.Utils.Primitives.HTLCId
        LocalParams: DotNetLightning.Channel.LocalParams
        OriginChannels: Map<DotNetLightning.Utils.Primitives.HTLCId, DotNetLightning.Channel.HTLCSource>
        RemoteChanges: DotNetLightning.Channel.RemoteChanges
        RemoteCommit: DotNetLightning.Channel.RemoteCommit
        RemoteNextCommitInfo: DotNetLightning.Channel.RemoteNextCommitInfo
        RemoteNextHTLCId: DotNetLightning.Utils.Primitives.HTLCId
        RemoteParams: DotNetLightning.Channel.RemoteParams
        RemotePerCommitmentSecrets: DotNetLightning.Crypto.RevocationSet
    }

type private CommitmentsJsonConverter() =
    inherit JsonConverter<DotNetLightning.Channel.Commitments>()

    override __.ReadJson(reader: JsonReader, _: Type, _: DotNetLightning.Channel.Commitments, _: bool, serializer: JsonSerializer) =
        let serializedCommitments = serializer.Deserialize<SerializedCommitments> reader
        let commitments: DotNetLightning.Channel.Commitments = {
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

    override __.WriteJson(writer: JsonWriter, state: DotNetLightning.Channel.Commitments, serializer: JsonSerializer) =
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

[<CustomEquality;CustomComparison>]
type GNodeId = | GNodeId of PubKey with
    member x.Value = let (GNodeId v) = x in v
    interface IComparable with
        override this.CompareTo(other) =
            match other with
            | :? GNodeId as n -> this.Value.CompareTo(n.Value)
            | _ -> -1
    override this.Equals(other) =
        match other with
        | :? GNodeId as n -> this.Value.Equals(n.Value)
        | _              -> false
    override this.GetHashCode() =
        this.Value.GetHashCode()

type IState = interface end
type IStateData = interface end
type IChannelStateData =
    interface inherit IStateData end
type IHasCommitments =
    inherit IChannelStateData
    abstract member ChannelId: DotNetLightning.Utils.ChannelId
    abstract member Commitments: DotNetLightning.Channel.Commitments

type Prism<'a, 'b> =
    ('a -> 'b option) * ('b -> 'a -> 'a)

type Lens<'a, 'b> =
    ('a -> 'b) * ('b -> 'a -> 'a)

/// Absolute block height
type BlockHeight = | BlockHeight of uint32 with
    static member Zero = 0u |> BlockHeight
    static member One = 1u |> BlockHeight
    member x.Value = let (BlockHeight v) = x in v
    member x.AsOffset() =
        x.Value |> Checked.uint16 |> BlockHeightOffset16

    static member (+) (a: BlockHeight, b: BlockHeightOffset16) =
            a.Value + (uint32 b.Value ) |> BlockHeight
    static member (+) (a: BlockHeight, b: BlockHeightOffset32) =
            a.Value + b.Value |> BlockHeight

    static member (-) (a: BlockHeight, b: BlockHeightOffset16) =
        a.Value - (uint32 b.Value) |> BlockHeight
    static member (-) (a: BlockHeight, b: BlockHeightOffset32) =
        a.Value - b.Value |> BlockHeight

    static member (-) (a: BlockHeight, b: BlockHeight) =
        a.Value - (b.Value) |> BlockHeightOffset32

/// **Description**
///
/// 16bit relative block height used for `OP_CSV` locks,
/// Since OP_CSV allow only block number of 0 ~ 65535, it is safe
/// to restrict into the range smaller than BlockHeight
and BlockHeightOffset16 = | BlockHeightOffset16 of uint16 with
    member x.Value = let (BlockHeightOffset16 v) = x in v

    static member ofBlockHeightOffset32(bho32: BlockHeightOffset32) =
        BlockHeightOffset16 (uint16 bho32.Value)
    static member op_Implicit (v: uint16) =
        BlockHeightOffset16 v
    static member One = BlockHeightOffset16(1us)
    static member Zero = BlockHeightOffset16(0us)
    static member MaxValue = UInt16.MaxValue |> BlockHeightOffset16
    static member (+) (a: BlockHeightOffset16, b: BlockHeightOffset16) =
        a.Value + b.Value |> BlockHeightOffset16
    static member (-) (a: BlockHeightOffset16, b: BlockHeightOffset16) =
        a.Value - b.Value |> BlockHeightOffset16

/// **Description**
///
/// 32bit relative block height. For `OP_CSV` locks, BlockHeightOffset16
/// should be used instead.
and BlockHeightOffset32 = | BlockHeightOffset32 of uint32 with
    member x.Value = let (BlockHeightOffset32 v) = x in v

    static member ofBlockHeightOffset16(bho16: BlockHeightOffset16) =
        BlockHeightOffset32 (uint32 bho16.Value)
    static member op_Implicit (v: uint32) =
        BlockHeightOffset32 v
    static member One = BlockHeightOffset32(1u)
    static member Zero = BlockHeightOffset32(0u)
    static member MaxValue = UInt32.MaxValue |> BlockHeightOffset32
    static member (+) (a: BlockHeightOffset32, b: BlockHeightOffset32) =
        a.Value + b.Value |> BlockHeightOffset32
    static member (-) (a: BlockHeightOffset32, b: BlockHeightOffset32) =
        a.Value - b.Value |> BlockHeightOffset32

type TxOutIndex = | TxOutIndex of uint16 with
    member x.Value = let (TxOutIndex v) = x in v

type TxIndexInBlock = | TxIndexInBlock of uint32 with
    member x.Value = let (TxIndexInBlock v) = x in v

[<StructuredFormatDisplay("{AsString}")>]
type ShortChannelId = {
    BlockHeight: BlockHeight
    BlockIndex: TxIndexInBlock
    TxOutIndex: TxOutIndex
}
    with
    
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
                BlockHeight = h |> BlockHeight
                BlockIndex = blockI |> TxIndexInBlock
                TxOutIndex = outputI |> TxOutIndex
            } |> Ok
        | _ -> err

type NormalData =   {
                            Commitments: DotNetLightning.Channel.Commitments;
                            ShortChannelId: ShortChannelId;
                            Buried: bool;
                            ChannelAnnouncement: DotNetLightning.Serialize.Msgs.ChannelAnnouncementMsg option
                            ChannelUpdate: DotNetLightning.Serialize.Msgs.ChannelUpdateMsg
                            LocalShutdown: DotNetLightning.Serialize.Msgs.ShutdownMsg option
                            RemoteShutdown: DotNetLightning.Serialize.Msgs.ShutdownMsg option
                            ChannelId: DotNetLightning.Utils.ChannelId
                        }
        with
            static member Commitments_: Lens<_, _> =
                (fun nd -> nd.Commitments), (fun v nd -> { nd with Commitments = v })

            interface IHasCommitments with
                member this.ChannelId: DotNetLightning.Utils.ChannelId = 
                    this.ChannelId
                member this.Commitments: DotNetLightning.Channel.Commitments = 
                    this.Commitments

type ChannelStatePhase =
    | Opening
    | Normal
    | Closing
    | Closed
    | Abnormal

type GChannelState =
    /// normal
    | Normal of NormalData

    /// Closing
    | Closing

    /// Abnormal
    | Offline of IChannelStateData

    with
        interface IState 

        static member Normal_: Prism<_, _> =
            (fun cc -> match cc with
                       | Normal s -> Some s
                       | _ -> None ),
            (fun v cc -> match cc with
                         | Normal _ -> Normal v
                         | _ -> cc )
        member this.ChannelId: Option<DotNetLightning.Utils.ChannelId> =
            match this with
            | Normal data -> Some data.ChannelId
            | Closing
            | Offline _ -> None

        member this.Phase =
            match this with
            | Normal _ -> ChannelStatePhase.Normal
            | Closing _ -> ChannelStatePhase.Closing
            | Offline _ -> Abnormal

        member this.Commitments: Option<DotNetLightning.Channel.Commitments> =
            match this with
            | Normal data -> Some (data :> IHasCommitments).Commitments
            | Closing
            | Offline _ -> None

type SerializedChannel =
    {
        ChannelIndex: int
        //Network: Network
        ChanState: GChannelState
        AccountFileName: string
        // FIXME: should store just RemoteNodeEndPoint instead of CounterpartyIP+RemoteNodeId?
        //CounterpartyIP: IPEndPoint
        RemoteNodeId: GNodeId
        // this is the amount of confirmations that the counterparty told us that the funding transaction needs
        //MinSafeDepth: BlockHeightOffset32
    }
    static member LightningSerializerSettings: JsonSerializerSettings =
        let settings = JsonMarshalling.SerializerSettings
        let commitmentsConverter = CommitmentsJsonConverter()
        settings.Converters.Add commitmentsConverter
        settings


module ChannelSerialization =
    let internal Commitments (serializedChannel: SerializedChannel): DotNetLightning.Channel.Commitments =
        UnwrapOption
            serializedChannel.ChanState.Commitments
            "A SerializedChannel is only created once a channel has started \
            being established and must therefore have an initial commitment"

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
    let internal MaxBalance (_: SerializedChannel): LNMoney =
        failwith "tmp:NIE"

    let ChannelId (serializedChannel: SerializedChannel): ChannelIdentifier =
        ChannelIdentifier.FromDnl (Commitments serializedChannel).ChannelId

