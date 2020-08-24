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

type PaymentHash = | PaymentHash of uint256 with
    member x.Value = let (PaymentHash v) = x in v
    member x.ToBytes(?lEndian) =
        let e = defaultArg lEndian false
        x.Value.ToBytes(e)

    member x.GetRIPEMD160() =
        let b = x.Value.ToBytes() |> Array.rev
        Crypto.Hashes.RIPEMD160(b, b.Length)


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

[<CLIMutable;StructuralComparison;StructuralEquality>]
type OnionPacket =
    {
        mutable Version: uint8
        /// This might be 33 bytes of 0uy in case of last packet
        /// So we are not using `PubKey` to represent pubkey
        mutable PublicKey: byte[]
        mutable HopData: byte[]
        mutable HMAC: uint256
    }
    with

        member this.IsLastPacket =
            this.HMAC = uint256.Zero

[<CLIMutable;StructuralComparison;StructuralEquality>]
type UpdateAddHTLCMsg = {
    mutable ChannelId: GChannelId
    mutable HTLCId: HTLCId
    mutable Amount: LNMoney
    mutable PaymentHash: PaymentHash
    mutable CLTVExpiry: BlockHeight
    mutable OnionRoutingPacket: OnionPacket
}

type DirectedHTLC = internal {
    Direction: Direction
    Add: UpdateAddHTLCMsg
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


type FinalizedTx =
    FinalizedTx of Transaction
    with
        member this.Value = let (FinalizedTx v) = this in v

type PublishableTxs = {
    CommitTx: FinalizedTx
    HTLCTxs: FinalizedTx list
}

type HTLCSuccessTx = {
    Value: PSBT
    WhichInput: int
    PaymentHash: PaymentHash
}

type CommitmentSpec = {
    HTLCs: Map<HTLCId, DirectedHTLC>
    FeeRatePerKw: FeeRatePerKw
    ToLocal: LNMoney
    ToRemote: LNMoney
}
    with

        member this.TotalFunds =
            this.ToLocal + this.ToRemote + (this.HTLCs |> Seq.sumBy(fun h -> h.Value.Add.Amount))


type LocalCommit = {
    Index: DotNetLightning.Utils.Primitives.CommitmentNumber
    Spec: CommitmentSpec
    PublishableTxs: PublishableTxs
    /// These are not redeemable on-chain until we get a corresponding preimage.
    PendingHTLCSuccessTxs: HTLCSuccessTx list
}

[<StructuralComparison;StructuralEquality>]
type GTxId = | GTxId of uint256 with
    member x.Value = let (GTxId v) = x in v
    static member Zero = uint256.Zero |> GTxId




type RemoteCommit = {
    Index: DotNetLightning.Utils.Primitives.CommitmentNumber
    Spec: CommitmentSpec
    TxId: GTxId
    RemotePerCommitmentPoint: DotNetLightning.Utils.Primitives.CommitmentPubKey
}


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

type LocalParams = {
    NodeId: GNodeId
    ChannelPubKeys: DotNetLightning.Chain.ChannelPubKeys
    DustLimitSatoshis: Money
    MaxHTLCValueInFlightMSat: LNMoney
    ChannelReserveSatoshis: Money
    HTLCMinimumMSat: LNMoney
    ToSelfDelay: BlockHeightOffset16
    MaxAcceptedHTLCs: uint16
    IsFunder: bool
    DefaultFinalScriptPubKey: Script
    Features: DotNetLightning.Serialize.FeatureBit
}

type RemoteParams = {
    NodeId: GNodeId
    DustLimitSatoshis: Money
    MaxHTLCValueInFlightMSat: LNMoney
    ChannelReserveSatoshis: Money
    HTLCMinimumMSat: LNMoney
    ToSelfDelay: BlockHeightOffset16
    MaxAcceptedHTLCs: uint16
    PaymentBasePoint: PubKey
    FundingPubKey: PubKey
    RevocationBasePoint: PubKey
    DelayedPaymentBasePoint: PubKey
    HTLCBasePoint: PubKey
    Features: DotNetLightning.Serialize.FeatureBit
    MinimumDepth: BlockHeightOffset32
}


[<CustomEquality;CustomComparison;StructuredFormatDisplay("{AsString}")>]
type LNECDSASignature = LNECDSASignature of NBitcoin.Crypto.ECDSASignature | Empty with
    member x.Value = match x with LNECDSASignature s -> s | Empty -> failwith "Unreachable!"
    override this.GetHashCode() = hash this.Value
    override this.Equals(obj: obj) =
        match obj with
        | :? LNECDSASignature as o -> (this :> IEquatable<LNECDSASignature>).Equals(o)
        | _ -> false
    interface IEquatable<LNECDSASignature> with
        member this.Equals(o: LNECDSASignature) =
            Utils.ArrayEqual(o.ToBytesCompact(), this.ToBytesCompact())
            

    override this.ToString() =
        SPrintF1 "LNECDSASignature (%A)" (this.ToBytesCompact())
    member this.AsString = this.ToString()

    /// ** Description **
    ///
    /// Bitcoin Layer 1 forces (by consensus) DER encoding for the signatures.
    /// This is not optimal, but remaining as a rule since changing consensus is not easy.
    /// However in layer2, there are no such rules. So we use more optimal serialization by
    /// This function.
    /// Note it does not include the recovery id. so its always 64 bytes
    ///
    /// **Output**
    ///
    /// (serialized R value + S value) in byte array.
    member this.ToBytesCompact() =
        this.Value.ToCompact()

    /// Logic does not really matter here. This is just for making life easier by enabling automatic implementation
    /// of `StructuralComparison` for wrapper types.
    member this.CompareTo(e: LNECDSASignature) =
        let a = this.ToBytesCompact() |> fun x -> Utils.ToUInt64(x, true)
        let b = e.ToBytesCompact() |>  fun x -> Utils.ToUInt64(x, true)
        a.CompareTo(b)
    interface IComparable with
        member this.CompareTo(o: obj) =
            match o with
            | :? LNECDSASignature as e -> this.CompareTo(e)
            | _ -> -1
            
    member this.ToDER() =
        this.Value.ToDER()
        
    /// Read 64 bytes as r(32 bytes) and s(32 bytes) value
    /// If `withRecId` is `true`, skip first 1 byte
    static member FromBytesCompact(bytes: byte [], ?withRecId: bool) =
        let withRecId = defaultArg withRecId false
        if withRecId && bytes.Length <> 65 then
            invalidArg "bytes" "ECDSASignature specified to have recovery id, but it was not 65 bytes length"
        else if (not withRecId) && bytes.Length <> 64 then
            invalidArg "bytes" "ECDSASignature was not specified to have recovery id, but it was not 64 bytes length."
        else
            let data = if withRecId then bytes.[1..] else bytes
            match NBitcoin.Crypto.ECDSASignature.TryParseFromCompact data with
            | true, x -> LNECDSASignature x
            | _ -> failwith "failed to parse compact ecdsa signature __" //data

    static member op_Implicit (ec: NBitcoin.Crypto.ECDSASignature) =
        ec |> LNECDSASignature


[<CLIMutable>]
type CommitmentSignedMsg = {
    mutable ChannelId: GChannelId
    mutable Signature: LNECDSASignature
    mutable HTLCSignatures: LNECDSASignature list
}

type WaitingForRevocation = {
    NextRemoteCommit: RemoteCommit
    Sent: CommitmentSignedMsg
    SentAfterLocalCommitmentIndex: DotNetLightning.Utils.Primitives.CommitmentNumber
    ReSignASAP: bool
}

type RemoteNextCommitInfo =
    | Waiting of WaitingForRevocation
    | Revoked of DotNetLightning.Utils.Primitives.CommitmentPubKey

type SerializedCommitments =
    {
        ChannelId: ChannelIdentifier
        ChannelFlags: uint8
        FundingScriptCoin: ScriptCoin
        LocalChanges: DotNetLightning.Channel.LocalChanges
        LocalCommit: LocalCommit
        LocalNextHTLCId: HTLCId
        LocalParams: LocalParams
        OriginChannels: Map<HTLCId, DotNetLightning.Channel.HTLCSource>
        RemoteChanges: DotNetLightning.Channel.RemoteChanges
        RemoteCommit: RemoteCommit
        RemoteNextCommitInfo: RemoteNextCommitInfo
        RemoteNextHTLCId: HTLCId
        RemoteParams: RemoteParams
        RemotePerCommitmentSecrets: GRevocationSet
    }

type Commitments = {
    LocalParams: LocalParams
    RemoteParams: RemoteParams
    ChannelFlags: uint8
    FundingScriptCoin: ScriptCoin
    LocalCommit: LocalCommit
    RemoteCommit: RemoteCommit
    LocalChanges: DotNetLightning.Channel.LocalChanges
    RemoteChanges: DotNetLightning.Channel.RemoteChanges
    LocalNextHTLCId: HTLCId
    RemoteNextHTLCId: HTLCId
    OriginChannels: Map<HTLCId, DotNetLightning.Channel.HTLCSource>
    RemoteNextCommitInfo: RemoteNextCommitInfo
    RemotePerCommitmentSecrets: GRevocationSet
    ChannelId: GChannelId
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
        let comm: SerializedCommitments =
            {
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
            }
        serializer.Serialize(writer, comm)




type IState = interface end
type IStateData = interface end
type IChannelStateData =
    interface inherit IStateData end

type Prism<'a, 'b> =
    ('a -> 'b option) * ('b -> 'a -> 'a)

type Lens<'a, 'b> =
    ('a -> 'b) * ('b -> 'a -> 'a)

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


        (*  
module BitArrayEx =
    let ToByteArray (ba: System.Collections.BitArray) =
        if ba.Length = 0 then [||] else

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
                let shortenedBitArray = System.Collections.BitArray(desiredLength)
                for i in 0 .. (desiredLength - 1) do
                    shortenedBitArray.[i] <- ba.[i - difference]
                shortenedBitArray
            else if difference > 0 then
                // Push zeroes to the front of the array until we have a multiple of 8 bits
                let lengthenedBitArray = System.Collections.BitArray(desiredLength)
                for i in 0 .. (ba.Length - 1) do
                    lengthenedBitArray.[i + difference] <- ba.[i]
                lengthenedBitArray
            else
                ba

        // Copy the bit array to a byte array, then flip the bytes.
        let byteArray: byte[] = Array.zeroCreate(desiredLength / 8)
        bitArray.CopyTo(byteArray, 0)
        failwith "tmp:NIE"
    let FromBytes(ba: byte[]) =
        ba |> Array.map(fun b -> b.FlipBit()) |> BitArray

[<StructuredFormatDisplay("{PrettyPrint}")>]
type FeatureBit private (bitArray) =
    member val BitArray: System.Collections.BitArray = bitArray with get, set
    member this.ByteArray
        with get() =
            BitArrayEx.ToByteArray bitArray
        and set(bytes: byte[]) =
            this.BitArray <- System.Collections.BitArray.FromBytes(bytes)
    static member Zero =
        let b: bool array = [||]
        b |> System.Collections.BitArray |> FeatureBit
    static member TryCreate(bytes: byte[]) =
        FeatureBit.TryCreate(BitArray.FromBytes(bytes))

    static member TryCreate(v: int64) =
        BitArray.FromInt64(v) |> FeatureBit.TryCreate
        
    static member CreateUnsafe(v: int64) =
        BitArray.FromInt64(v) |> FeatureBit.CreateUnsafe
        
    static member private Unwrap(r: Result<FeatureBit, _>) =
        match r with
        | Error(FeatureError.UnknownRequiredFeature(e))
        | Error(FeatureError.BogusFeatureDependency(e)) -> raise <| FormatException(e)
        | Ok fb -> fb
    /// Throws FormatException
    /// TODO: ugliness of this method is caused by binary serialization throws error instead of returning Result
    /// We should refactor serialization altogether at some point
    static member CreateUnsafe(bytes: byte[]) =
        FeatureBit.TryCreate bytes |> FeatureBit.Unwrap
        
    static member CreateUnsafe(ba: BitArray) =
        FeatureBit.TryCreate ba |> FeatureBit.Unwrap
    static member TryParse(str: string) =
        result {
            let! ba = BitArray.TryParse str
            return! ba |> FeatureBit.TryCreate |> Result.mapError(fun fe -> fe.ToString())
        }
        
    override this.ToString() =
        this.BitArray.PrintBits()
        
    member this.SetFeature(feature: Feature) (support: FeaturesSupport) (on: bool): unit =
        let index = feature.BitPosition support
        let length = this.BitArray.Length
        if length <= index then
            this.BitArray.Length <- index + 1

            //this.BitArray.RightShift(index - length + 1)

            // NOTE: Calling RightShift gives me:
            // "The field, constructor or member 'RightShift' is not defined."
            // So I just re-implement it here
            for i in (length - 1) .. -1 .. 0 do
                this.BitArray.[i + index - length + 1] <- this.BitArray.[i]

            // NOTE: this probably wouldn't be necessary if we were using
            // RightShift, but the dotnet docs don't actualy specify that
            // RightShift sets the leading bits to zero.
            for i in 0 .. (index - length) do
                this.BitArray.[i] <- false
        this.BitArray.[this.BitArray.Length - index - 1] <- on

    member this.HasFeature(f, ?featureType) =
        Feature.hasFeature this.BitArray (f) (featureType)
        
    member this.PrettyPrint =
        let sb = StringBuilder()
        let reversed = this.BitArray.Reverse()
        for f in Feature.allFeatures do
            if (reversed.Length > f.MandatoryBitPosition) && (reversed.[f.MandatoryBitPosition]) then
                sb.Append(SPrintF1 "%s is mandatory. " f.RfcName) |> ignore
            else if (reversed.Length > f.OptionalBitPosition) && (reversed.[f.OptionalBitPosition]) then
                sb.Append(SPrintF1 "%s is optional. " f.RfcName) |> ignore
            else
                sb.Append(SPrintF1 "%s is non supported. " f.RfcName) |> ignore
        sb.ToString()
    
    member this.ToByteArray() = this.ByteArray
        
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
        for i in this.BitArray do
            num <- -1640531527 + i.GetHashCode() + ((num <<< 6) + (num >>> 2))
        num
        
    member this.CompareTo(o: FeatureBit) =
        if (this.BitArray.Length > o.BitArray.Length) then -1 else
        if (this.BitArray.Length < o.BitArray.Length) then 1 else
        let mutable result = 0
        for i in 0..this.BitArray.Length - 1 do
            if      (this.BitArray.[i] > o.BitArray.[i]) then
                result <- -1
            else if (this.BitArray.[i] < o.BitArray.[i]) then
                result <- 1
        result
    interface IComparable with
        member this.CompareTo(o) =
            match o with
            | :? FeatureBit as fb -> this.CompareTo(fb)
            | _ -> -1
    // --------
*)

[<CustomEquality;CustomComparison>]
type NodeId = | NodeId of PubKey with
    member x.Value = let (NodeId v) = x in v
    interface IComparable with
        override this.CompareTo(other) =
            match other with
            | :? NodeId as n -> this.Value.CompareTo(n.Value)
            | _ -> -1
    override this.Equals(other) =
        match other with
        | :? NodeId as n -> this.Value.Equals(n.Value)
        | _              -> false
    override this.GetHashCode() =
        this.Value.GetHashCode()

[<CustomEquality;CustomComparison>]
type ComparablePubKey = ComparablePubKey of PubKey with
    member x.Value = let (ComparablePubKey v) = x in v
    interface IComparable with
        override this.CompareTo(other) =
            match other with
            | :? ComparablePubKey as n -> this.Value.CompareTo(n.Value)
            | _ -> -1
    override this.GetHashCode() = this.Value.GetHashCode()
    override this.Equals(other) =
        match other with
        | :? ComparablePubKey as n -> this.Value.Equals(n.Value)
        | _              -> false
    static member op_Implicit (pk: PubKey) =
        pk |> ComparablePubKey

[<StructuralComparison;StructuralEquality;CLIMutable>]
type UnsignedChannelAnnouncementMsg = {
    mutable Features: DotNetLightning.Serialize.FeatureBit
    mutable ChainHash: uint256
    mutable ShortChannelId: ShortChannelId
    mutable NodeId1: NodeId
    mutable NodeId2: NodeId
    mutable BitcoinKey1: ComparablePubKey
    mutable BitcoinKey2: ComparablePubKey
    mutable ExcessData: byte[]
}

[<CLIMutable>]
type ChannelAnnouncementMsg = {
    mutable NodeSignature1: LNECDSASignature
    mutable NodeSignature2: LNECDSASignature
    mutable BitcoinSignature1: LNECDSASignature
    mutable BitcoinSignature2: LNECDSASignature
    mutable Contents: UnsignedChannelAnnouncementMsg
}

[<CLIMutable>]
type UnsignedChannelUpdateMsg = {
    mutable ChainHash: uint256
    mutable ShortChannelId: ShortChannelId
    mutable Timestamp: uint32
    mutable MessageFlags: uint8
    mutable ChannelFlags: uint8
    mutable CLTVExpiryDelta: BlockHeightOffset16
    mutable HTLCMinimumMSat: LNMoney
    mutable FeeBaseMSat: LNMoney
    mutable FeeProportionalMillionths: uint32
    mutable HTLCMaximumMSat: Option<LNMoney>
}

[<CLIMutable>]
type ShutdownMsg = {
    mutable ChannelId: GChannelId
    mutable ScriptPubKey: Script
}

[<CLIMutable>]
type ChannelUpdateMsg = {
    mutable Signature: LNECDSASignature
    mutable Contents: UnsignedChannelUpdateMsg
}
with
    member this.IsNode1 =
        (this.Contents.ChannelFlags &&& 1uy) = 0uy



type IHasCommitments =
    inherit IChannelStateData
    abstract member ChannelId: GChannelId
    abstract member Commitments: Commitments

type NormalData =   {
                            Commitments: Commitments;
                            ShortChannelId: ShortChannelId;
                            Buried: bool;
                            ChannelAnnouncement: ChannelAnnouncementMsg option
                            ChannelUpdate: ChannelUpdateMsg
                            LocalShutdown: ShutdownMsg option
                            RemoteShutdown: ShutdownMsg option
                            ChannelId: GChannelId
                        }
        with
            static member Commitments_: Lens<_, _> =
                (fun nd -> nd.Commitments), (fun v nd -> { nd with Commitments = v })

            interface IHasCommitments with
                member this.ChannelId: GChannelId = 
                    this.ChannelId
                member this.Commitments: Commitments = 
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
        member this.ChannelId: Option<GChannelId> =
            match this with
            | Normal data -> Some data.ChannelId
            | Closing
            | Offline _ -> None

        member this.Phase =
            match this with
            | Normal _ -> ChannelStatePhase.Normal
            | Closing _ -> ChannelStatePhase.Closing
            | Offline _ -> Abnormal

        member this.Commitments: Option<Commitments> =
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
    let internal Commitments (serializedChannel: SerializedChannel): Commitments =
        UnwrapOption
            serializedChannel.ChanState.Commitments
            "A SerializedChannel is only created once a channel has started \
            being established and must therefore have an initial commitment"

    let IsFunder (serializedChannel: SerializedChannel): bool =
        (Commitments serializedChannel).LocalParams.IsFunder

    let internal Capacity (serializedChannel: SerializedChannel): Money =
        (Commitments serializedChannel).FundingScriptCoin.Amount

    let internal Balance (_: SerializedChannel): LNMoney =
        failwith "tmp:NIE"

    let internal SpendableBalance (_: SerializedChannel): LNMoney =
        failwith "tmp:NIE"

    // How low the balance can go. A channel must maintain enough balance to
    // cover the channel reserve. The funder must also keep enough in the
    // channel to cover the closing fee.
    let internal MinBalance (serializedChannel: SerializedChannel): LNMoney =
        (Balance serializedChannel) - (SpendableBalance serializedChannel)

    // How high the balance can go. The fundee will only be able to receive up
    // to this amount before the funder no longer has enough funds to cover
    // the channel reserve and closing fee.
    let internal MaxBalance (_: SerializedChannel): LNMoney =
        failwith "tmp:NIE"

    let ChannelId (_: SerializedChannel): ChannelIdentifier =
        failwith "tmp:NIE"

