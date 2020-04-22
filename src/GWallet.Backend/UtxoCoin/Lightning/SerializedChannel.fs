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
open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks
open GWallet.Backend.UtxoCoin.Lightning.Util

open Newtonsoft.Json

type SerializedCommitmentSpec = {
    HTLCs: Map<HTLCId, DirectedHTLC>
    FeeRatePerKw: FeeRatePerKw
    ToLocal: LNMoney
    ToRemote: LNMoney
}

type SerializedCommitments = {
    ChannelId: ChannelId
    ChannelFlags: uint8
    FundingSCoin: ScriptCoin
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
    RemotePerCommitmentSecrets: ShaChain
}

type private CommitmentsJsonConverter() =
    inherit JsonConverter<Commitments>()

    override this.ReadJson(reader: JsonReader, _: Type, _: Commitments, _: bool, serializer: JsonSerializer) =
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
            FundingSCoin = serializedCommitments.FundingSCoin
            ChannelId = serializedCommitments.ChannelId
            ChannelFlags = serializedCommitments.ChannelFlags
        }
        commitments

    override this.WriteJson(writer: JsonWriter, state: Commitments, serializer: JsonSerializer) =
        serializer.Serialize(writer, {
            ChannelId = state.ChannelId
            ChannelFlags = state.ChannelFlags
            FundingSCoin = state.FundingSCoin
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

type SerializedChannel = {
    ChannelIndex: int
    Network: Network
    RemoteNodeId: PubKey
    ChanState: ChannelState
    AccountFileName: string
    CounterpartyIP: IPEndPoint
    // this is the amount of confirmations that the counterparty told us that the funding transaction needs
    MinSafeDepth: BlockHeightOffset32
} with
    static member ChannelFilePrefix = "chan-"
    static member ChannelFileEnding = ".json"

    static member LightningSerializerSettings: JsonSerializerSettings =
        let settings = JsonMarshalling.SerializerSettings
        let commitmentsConverter = CommitmentsJsonConverter()
        settings.Converters.Add commitmentsConverter
        settings

    static member FileNameForChannelId (channelId: ChannelId) =
        Path.Combine(
            LightningConfig.LightningDir.FullName,
            SPrintF3
                "%s%s%s"
                SerializedChannel.ChannelFilePrefix
                (channelId.Value.ToString())
                SerializedChannel.ChannelFileEnding
        )

    static member ListSavedChannels(): seq<ChannelId> =
        let extractChannelId path =
            let fileName = Path.GetFileName path
            let withoutPrefix = fileName.Substring SerializedChannel.ChannelFilePrefix.Length
            let withoutEnding =
                withoutPrefix.Substring(
                    0,
                    withoutPrefix.Length - SerializedChannel.ChannelFileEnding.Length
                )
            try
                Some(ChannelId(uint256 withoutEnding))
            with
            | :? FormatException ->
                None

        if LightningConfig.LightningDir.Exists then
            let files =
                Directory.GetFiles(LightningConfig.LightningDir.ToString())
            files |> Seq.choose extractChannelId
        else
            Seq.empty

    static member LoadFromWallet (channelId: ChannelId) =
        let fileName = SerializedChannel.FileNameForChannelId channelId
        DebugLogger <| SPrintF2 "loading file %s for %s" fileName (channelId.Value.ToString())
        let json = File.ReadAllText fileName
        Marshalling.DeserializeCustom<SerializedChannel> (
            json,
            SerializedChannel.LightningSerializerSettings
        )

    member this.Balance(): Option<LNMoney> =
        match this.ChanState.Commitments with
        | Some commitments -> Some commitments.LocalCommit.Spec.ToLocal
        | None -> None

    member this.SpendableBalance(): Option<LNMoney> =
        this.ChanState.SpendableBalance

    member this.ChannelId
        with get(): ChannelId = this.ChanState.ChannelId.Value

    member this.SaveToWallet() =
        let fileName = SerializedChannel.FileNameForChannelId this.ChannelId
        let json = Marshalling.SerializeCustom(this, SerializedChannel.LightningSerializerSettings)
        if not LightningConfig.LightningDir.Exists then
            LightningConfig.LightningDir.Create()
        File.WriteAllText(fileName, json)

