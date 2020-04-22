namespace GWallet.Backend.UtxoCoin.Lightning

open System

open NBitcoin

open DotNetLightning.Utils
open DotNetLightning.Channel
open DotNetLightning.Chain

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks
open FSharp.Core

type ChannelWrapper = {
    Channel: Channel
} with
    static member Create (nodeId: NodeId)
                         (shutdownScriptPubKey: Script)
                         (nodeSecret: ExtKey)
                         (channelIndex: int)
                         (fundingTxProvider: ProvideFundingTx)
                         (initialState: ChannelState)
                             : Async<ChannelWrapper> = async {
        let channelConfig =
            let handshakeConfig = {
                ChannelHandshakeConfig.MinimumDepth = BlockHeightOffset32 1u
            }
            let peerLimits: ChannelHandshakeLimits = {
                ForceChannelAnnouncementPreference = false
                MinFundingSatoshis = Money 100L
                MaxHTLCMinimumMSat = LNMoney 100000L
                MinMaxHTLCValueInFlightMSat = LNMoney 1000L
                MaxChannelReserveSatoshis = Money 100000L
                MinMaxAcceptedHTLCs = 1us
                MinDustLimitSatoshis = Money 1L
                MaxDustLimitSatoshis = Money 10000000L
                // TODO make optional in DotNetLightning
                MaxMinimumDepth = BlockHeightOffset32 UInt32.MaxValue
                MaxClosingNegotiationIterations = 10
            }
            let channelOptions = {
                AnnounceChannel = false
                FeeProportionalMillionths = 100u
                MaxFeeRateMismatchRatio = 1.
                ShutdownScriptPubKey = Some shutdownScriptPubKey
            }
            {
                ChannelHandshakeConfig = handshakeConfig
                PeerChannelConfigLimits = peerLimits
                ChannelOptions = channelOptions
            }
        let keyRepo = DefaultKeyRepository(nodeSecret, channelIndex)
        let! feeEstimator = FeeEstimator.Create()
        let network = Config.BitcoinNet
        let channel = 
            Channel.Create(
                channelConfig,
                keyRepo,
                feeEstimator,
                nodeSecret.PrivateKey,
                fundingTxProvider,
                network,
                nodeId
            )
        let channel = { channel with State = initialState }
        return { Channel = channel }
    }

    member this.RemoteNodeId
        with get(): NodeId = this.Channel.RemoteNodeId

    member this.Network
        with get(): Network = this.Channel.Network

    member this.ChannelId
        with get(): Option<ChannelId> = this.Channel.State.ChannelId

    member this.ChannelKeys
        with get(): ChannelKeys =
            this.Channel.KeysRepository.GetChannelKeys false

    member this.FundingTxId
        with get(): Option<TxId> =
            match this.Channel.State.Commitments with
            | Some commitments -> Some <| TxId commitments.FundingScriptCoin.Outpoint.Hash
            | None -> None

    member this.FundingScriptCoin
        with get(): Option<ScriptCoin> =
            match this.Channel.State.Commitments with
            | Some commitments -> Some commitments.FundingScriptCoin
            | None -> None

    member this.LocalParams (funding: Money)
                            (defaultFinalScriptPubKey: Script)
                            (isFunder: bool)
                                : LocalParams = {
        NodeId = this.RemoteNodeId
        ChannelPubKeys = this.ChannelKeys.ToChannelPubKeys()
        DustLimitSatoshis = Money 5UL
        MaxHTLCValueInFlightMSat = LNMoney 5000L
        ChannelReserveSatoshis = funding / 100L
        HTLCMinimumMSat = LNMoney 1000L
        ToSelfDelay = BlockHeightOffset16 6us
        MaxAcceptedHTLCs = uint16 10
        IsFunder = isFunder
        DefaultFinalScriptPubKey = defaultFinalScriptPubKey
        Features = MsgStream.SupportedFeatures
    }

    member this.ExecuteCommand<'T> (channelCmd: ChannelCommand)
                                   (eventFilter: List<ChannelEvent> -> Option<'T>)
                                       : Result<'T, ChannelError> * ChannelWrapper =
        let channel = this.Channel
        match Channel.executeCommand this.Channel channelCmd with
        | Error channelError -> (Error channelError), this
        | Ok evtList ->
            match (eventFilter evtList) with
            | Some value ->
                let rec apply (channel: Channel) (evtList: List<ChannelEvent>) =
                    match evtList with
                    | evt::rest ->
                        let channel = Channel.applyEvent channel evt
                        apply channel rest
                    | [] -> channel
                let channelAfterEventsApplied = apply channel evtList
                let channelWrapper = { this with Channel = channelAfterEventsApplied }
                (Ok value), channelWrapper
            | None ->
                failwith <| SPrintF2
                    "unexpected result executing channel command %A. got: %A"
                    channelCmd
                    evtList

    member this.Balance(): Option<LNMoney> =
        match this.Channel.State.Commitments with
        | Some commitments -> Some commitments.LocalCommit.Spec.ToLocal
        | None -> None

    member this.SpendableBalance(): Option<LNMoney> =
        match this.Channel.State.Commitments with
        | Some commitments -> Some <| commitments.SpendableBalance()
        | None -> None

