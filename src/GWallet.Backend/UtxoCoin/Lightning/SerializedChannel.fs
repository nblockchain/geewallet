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
}