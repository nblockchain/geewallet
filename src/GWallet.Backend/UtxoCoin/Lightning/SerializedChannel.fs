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

type CommitmentSpecConverter() =
    inherit JsonConverter<CommitmentSpec>()

    override this.ReadJson(reader: JsonReader, _: Type, _: CommitmentSpec, _: bool, serializer: JsonSerializer) =
        let serializedCommitmentSpec = serializer.Deserialize<SerializedCommitmentSpec> reader
        {
            CommitmentSpec.HTLCs = serializedCommitmentSpec.HTLCs
            FeeRatePerKw = serializedCommitmentSpec.FeeRatePerKw
            ToLocal = serializedCommitmentSpec.ToLocal
            ToRemote = serializedCommitmentSpec.ToRemote
        }

    override this.WriteJson(writer: JsonWriter, spec: CommitmentSpec, serializer: JsonSerializer) =
        let serializedCommitmentSpec =
            {
                SerializedCommitmentSpec.HTLCs = spec.HTLCs
                FeeRatePerKw = spec.FeeRatePerKw
                ToLocal = spec.ToLocal
                ToRemote = spec.ToRemote
            }
        serializer.Serialize(writer, serializedCommitmentSpec)

type CommitmentsJsonConverter() =
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
    KeysRepoSeed: uint256
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
    static member ChannelFilePrefix = "chan"
    static member ChannelFileEnding = ".json"

    static member UIntToKeyRepo (channelKeysSeed: uint256): DefaultKeyRepository =
        let littleEndian = channelKeysSeed.ToBytes()
        DefaultKeyRepository (ExtKey littleEndian, 0)

    static member ExtractChannelNumber (path: string): Option<string * int> =
        let fileName = Path.GetFileName path
        let withoutPrefix = fileName.Substring SerializedChannel.ChannelFilePrefix.Length
        let withoutEnding = withoutPrefix.Substring (0, withoutPrefix.Length - SerializedChannel.ChannelFileEnding.Length)
        match Int32.TryParse withoutEnding with
        | true, channelNumber ->
            Some (path, channelNumber)
        | false, _ ->
            None

    static member LightningSerializerSettings: JsonSerializerSettings =
        let settings = JsonMarshalling.LightningSerializerSettings
        let commitmentsConverter = CommitmentsJsonConverter()
        settings.Converters.Add commitmentsConverter
        settings

    static member ListSavedChannels (): seq<string * int> =
        if SerializedChannel.LightningDir.Exists then
            let files =
                Directory.GetFiles
                    ((SerializedChannel.LightningDir.ToString()), SerializedChannel.ChannelFilePrefix + "*" + SerializedChannel.ChannelFileEnding)
            files |> Seq.choose SerializedChannel.ExtractChannelNumber
        else
            Seq.empty

    member this.SaveSerializedChannel (fileName: string) =
        let json = Marshalling.SerializeCustom(this, SerializedChannel.LightningSerializerSettings)
        let filePath = Path.Combine (SerializedChannel.LightningDir.FullName, fileName)
        if not SerializedChannel.LightningDir.Exists then
            SerializedChannel.LightningDir.Create()
        File.WriteAllText (filePath, json)
        ()

    static member Save (account: NormalAccount)
                       (chan: Channel)
                       (keysRepoSeed: uint256)
                       (ip: IPEndPoint)
                       (minSafeDepth: BlockHeightOffset32)
                           : string -> unit =
        let serializedChannel =
            {
                KeysRepoSeed = keysRepoSeed
                Network = chan.Network
                RemoteNodeId = chan.RemoteNodeId.Value
                ChanState = chan.State
                AccountFileName = account.AccountFile.Name
                CounterpartyIP = ip
                MinSafeDepth = minSafeDepth
            }
        serializedChannel.SaveSerializedChannel

    static member LoadSerializedChannel (fileName: string): SerializedChannel =
        let json = File.ReadAllText fileName
        Marshalling.DeserializeCustom<SerializedChannel> (json, SerializedChannel.LightningSerializerSettings)

    member this.ChannelFromSerialized (createChannel) (feeEstimator): Channel =
        let DummyProvideFundingTx (_ : IDestination * Money * FeeRatePerKw): FSharp.Core.Result<(FinalizedTx * TxOutIndex),string> =
            FSharp.Core.Result.Error "funding tx not needed cause channel already created"
        let keyRepo = SerializedChannel.UIntToKeyRepo this.KeysRepoSeed
        {
            createChannel
                keyRepo
                feeEstimator
                keyRepo.NodeSecret.PrivateKey
                DummyProvideFundingTx
                this.Network
                (NodeId this.RemoteNodeId)
            with State = this.ChanState
        }
