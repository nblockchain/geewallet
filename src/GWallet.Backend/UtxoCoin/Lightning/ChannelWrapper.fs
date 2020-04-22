namespace GWallet.Backend.UtxoCoin.Lightning

open System

open NBitcoin

open DotNetLightning.Utils
open DotNetLightning.Channel
open DotNetLightning.Chain

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks
open FSharp.Core

type ChannelOperationError =
    | PeerErrorMessage of PeerErrorMessage
    | ChannelError of ChannelError

type FeeEstimator() =
    interface IFeeEstimator with
        member __.GetEstSatPer1000Weight(_: ConfirmationTarget) =
            // this one covers transactions that we alone can decide on an
            // arbitrary feerate for
            FeeRatePerKw 7500u

type ChannelWrapper = {
    Channel: Channel
} with
    static member Create (nodeId: NodeId)
                         (shutdownScriptPubKey: Script)
                         (nodeSecret: ExtKey)
                         (channelIndex: int)
                         (fundingTxProvider: ProvideFundingTx)
                         (initialState: ChannelState)
                             : ChannelWrapper =
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
        let feeEstimator = FeeEstimator() :> IFeeEstimator
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
        { Channel = channel }

    member this.RemoteNodeId
        with get(): NodeId = this.Channel.RemoteNodeId

    member this.Network
        with get(): Network = this.Channel.Network

    member this.ChannelId
        with get(): ChannelId = this.Channel.State.ChannelId.Value

    member this.ChannelKeys
        with get(): ChannelKeys =
            this.Channel.KeysRepository.GetChannelKeys false

    member this.FundingTxId
        with get(): TxId =
            let waitForFundingConfirmedData =
                match this.Channel.State with
                | WaitForFundingConfirmed waitForFundingConfirmedData ->
                    waitForFundingConfirmedData
                | _ ->
                    failwith <| SPrintF1
                        "expected channel state WaitForFundingConfirmed. got: %A"
                        this.Channel.State
            TxId waitForFundingConfirmedData.Commitments.FundingSCoin.Outpoint.Hash

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
                let channel = apply channel evtList
                let channelWrapper = { this with Channel = channel }
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
        this.Channel.State.SpendableBalance

