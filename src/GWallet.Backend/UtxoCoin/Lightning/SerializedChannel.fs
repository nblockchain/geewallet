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
            FundingScriptCoin = serializedCommitments.FundingScriptCoin
            ChannelId = serializedCommitments.ChannelId
            ChannelFlags = serializedCommitments.ChannelFlags
        }
        commitments

    override this.WriteJson(writer: JsonWriter, state: Commitments, serializer: JsonSerializer) =
        serializer.Serialize(writer, {
            ChannelId = state.ChannelId
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
    static member ConfigDir: DirectoryInfo =
        Config.GetConfigDir Currency.BTC AccountKind.Normal
    static member LightningDir: DirectoryInfo =
        Path.Combine (SerializedChannel.ConfigDir.FullName, "LN") |> DirectoryInfo
    static member ChannelFilePrefix = "chan-"
    static member ChannelFileEnding = ".json"

    static member LightningSerializerSettings: JsonSerializerSettings =
        let settings = JsonMarshalling.SerializerSettings
        let commitmentsConverter = CommitmentsJsonConverter()
        settings.Converters.Add commitmentsConverter
        settings

    static member FileNameForChannelId (channelId: ChannelId) =
        Path.Combine(
            SerializedChannel.LightningDir.FullName,
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

        if SerializedChannel.LightningDir.Exists then
            let files =
                Directory.GetFiles(SerializedChannel.LightningDir.ToString())
            files |> Seq.choose extractChannelId
        else
            Seq.empty

    static member LoadFromWallet (channelId: ChannelId): SerializedChannel =
        let fileName = SerializedChannel.FileNameForChannelId channelId
        let json = File.ReadAllText fileName
        Marshalling.DeserializeCustom<SerializedChannel> (
            json,
            SerializedChannel.LightningSerializerSettings
        )

    member this.Commitments: Commitments =
        UnwrapOption
            this.ChanState.Commitments
            "A SerializedChannel is only created once a channel has started \
            being established and must therefore have an initial commitment"

    member this.Balance(): LNMoney =
        this.Commitments.LocalCommit.Spec.ToLocal

    member this.SpendableBalance(): LNMoney =
        this.Commitments.SpendableBalance()

    member this.ChannelId: ChannelId =
        this.Commitments.ChannelId

    member this.IsFunder: bool =
        this.Commitments.LocalParams.IsFunder

    member this.Account(): UtxoCoin.NormalUtxoAccount =
        let accountFileName = Path.Combine(SerializedChannel.ConfigDir.FullName, this.AccountFileName)
        let fromAccountFileToPublicAddress =
            UtxoCoin.Account.GetPublicAddressFromNormalAccountFile Currency.BTC
        UtxoCoin.NormalUtxoAccount
            (
                Currency.BTC,
                {
                    Name = Path.GetFileName accountFileName
                    Content = fun _ -> File.ReadAllText accountFileName
                },
                fromAccountFileToPublicAddress,
                UtxoCoin.Account.GetPublicKeyFromNormalAccountFile
            )

    member this.SaveToWallet() =
        let fileName = SerializedChannel.FileNameForChannelId this.ChannelId
        let json = Marshalling.SerializeCustom(this, SerializedChannel.LightningSerializerSettings)
        if not SerializedChannel.LightningDir.Exists then
            SerializedChannel.LightningDir.Create()
        File.WriteAllText(fileName, json)

