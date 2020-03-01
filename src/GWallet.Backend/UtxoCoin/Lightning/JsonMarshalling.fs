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

open GWallet.Backend

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
    RemoteNextCommitInfo: Choice<WaitingForRevocation, PubKey>
    RemoteNextHTLCId: HTLCId
    RemoteParams: RemoteParams
    RemotePerCommitmentSecrets: ShaChain
}

type SerializedChannel = {
    KeysRepoSeed: uint256
    Network: Network
    RemoteNodeId: PubKey
    ChanState: ChannelState
    AccountFileName: string
    CounterpartyIP: IPEndPoint
    // this is the amount of confirmations that the counterparty told us that the funding transaction needs
    MinSafeDepth: BlockHeight
}


module JsonMarshalling =

    type CommitmentSpecConverter() =
        inherit JsonConverter<CommitmentSpec>()

        override this.ReadJson(reader: JsonReader, objectType: Type, existingValue: CommitmentSpec, hasExistingValue: bool, serializer: JsonSerializer) =
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

    type IPAddressJsonConverter() =
        inherit JsonConverter<IPAddress>()

        override this.ReadJson(reader: JsonReader, objectType: Type, existingValue: IPAddress, hasExistingValue: bool, serializer: JsonSerializer) =
            let serializedIPAddress = serializer.Deserialize<string> reader
            IPAddress.Parse serializedIPAddress

        override this.WriteJson(writer: JsonWriter, state: IPAddress, serializer: JsonSerializer) =
            serializer.Serialize(writer, state.ToString())

    type IPEndPointJsonConverter() =
        inherit JsonConverter<IPEndPoint>()

        override this.ReadJson(reader: JsonReader, objectType: Type, existingValue: IPEndPoint, hasExistingValue: bool, serializer: JsonSerializer) =
            assert (reader.TokenType = JsonToken.StartArray)
            reader.Read() |> ignore
            let ip = serializer.Deserialize<IPAddress> reader
            reader.Read() |> ignore
            let port = serializer.Deserialize<int32> reader
            assert (reader.TokenType = JsonToken.EndArray)
            reader.Read() |> ignore
            IPEndPoint (ip, port)

        override this.WriteJson(writer: JsonWriter, state: IPEndPoint, serializer: JsonSerializer) =
            writer.WriteStartArray()
            serializer.Serialize(writer, state.Address)
            serializer.Serialize(writer, state.Port)
            writer.WriteEndArray()

    type CommitmentsJsonConverter() =
        inherit JsonConverter<Commitments>()

        override this.ReadJson(reader: JsonReader, objectType: Type, existingValue: Commitments, hasExistingValue: bool, serializer: JsonSerializer) =
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

    let LightningSerializerSettings: JsonSerializerSettings =
        let settings = Marshalling.DefaultSettings ()
        let converter = CommitmentsJsonConverter()
        let ipAddressConverter = IPAddressJsonConverter()
        let ipEndPointConverter = IPEndPointJsonConverter()
        settings.Converters.Add converter
        settings.Converters.Add ipAddressConverter
        settings.Converters.Add ipEndPointConverter
        NBitcoin.JsonConverters.Serializer.RegisterFrontConverters settings
        settings

    let ConfigDir: DirectoryInfo = Config.GetConfigDir Currency.BTC AccountKind.Normal
    let LightningDir: DirectoryInfo = Path.Combine (ConfigDir.FullName, "LN") |> DirectoryInfo

    let SaveSerializedChannel (serializedChannel: SerializedChannel)
                              (fileName: string) =
        let json = Marshalling.SerializeCustom(serializedChannel, LightningSerializerSettings)
        let filePath = Path.Combine (LightningDir.FullName, fileName)
        if not LightningDir.Exists then
            LightningDir.Create()
        File.WriteAllText (filePath, json)
        ()

    let Save (account: NormalAccount)
             (chan: Channel)
             (keysRepoSeed: uint256)
             (ip: IPEndPoint)
             (minSafeDepth: BlockHeight)
                 : string -> unit =
        SaveSerializedChannel
            {
                KeysRepoSeed = keysRepoSeed
                Network = chan.Network
                RemoteNodeId = chan.RemoteNodeId.Value
                ChanState = chan.State
                AccountFileName = account.AccountFile.Name
                CounterpartyIP = ip
                MinSafeDepth = minSafeDepth
            }

    let LoadSerializedChannel (fileName: string): SerializedChannel =
        let json = File.ReadAllText fileName
        Marshalling.DeserializeCustom<SerializedChannel> (json, LightningSerializerSettings)

    let DummyProvideFundingTx (_ : IDestination * Money * FeeRatePerKw): FSharp.Core.Result<(FinalizedTx * TxOutIndex),string> =
        FSharp.Core.Result.Error "funding tx not needed cause channel already created"

    let ChannelFromSerialized (serializedChannel: SerializedChannel) (createChannel) (feeEstimator): Channel =
        let keyRepo = DefaultKeyRepository serializedChannel.KeysRepoSeed
        {
            createChannel
                keyRepo
                feeEstimator
                keyRepo.NodeSecret.PrivateKey
                DummyProvideFundingTx
                serializedChannel.Network
                (NodeId serializedChannel.RemoteNodeId)
            with State = serializedChannel.ChanState
        }
