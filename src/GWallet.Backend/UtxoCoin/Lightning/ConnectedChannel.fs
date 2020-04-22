namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Net
open System.IO

open NBitcoin

open DotNetLightning.Utils
open DotNetLightning.Serialize.Msgs
open DotNetLightning.Channel

open GWallet.Backend
open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks
open GWallet.Backend.UtxoCoin
open GWallet.Backend.UtxoCoin.Lightning.Util

open FSharp.Core

type ReestablishChannelError(peerErrorMessage: PeerErrorMessage) =
    inherit Exception(SPrintF1 "error reestablishing channel: %s" (peerErrorMessage.ToString()))

type ConnectedChannel = {
    PeerWrapper: PeerWrapper
    ChannelWrapper: ChannelWrapper
    Account: NormalUtxoAccount
    MinimumDepth: BlockHeightOffset32
    ChannelIndex: int
} with
    interface IDisposable with
        member this.Dispose() =
            (this.PeerWrapper :> IDisposable).Dispose()

    static member LoadFromWallet (transportListener: TransportListener)
                                 (channelId: ChannelId)
                                 (initiateConnection: bool)
                                     : Async<ConnectedChannel> = async {
        let serializedChannel = SerializedChannel.LoadFromWallet channelId
        DebugLogger <| SPrintF1 "loading account for %s" (channelId.Value.ToString())
        let account =
            let accountFileName = serializedChannel.AccountFileName
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
        DebugLogger <| SPrintF1 "loading channel for %s" (channelId.Value.ToString())
        let channelWrapper =
            let fundingTxProvider (_ : IDestination * Money * FeeRatePerKw) =
                Result.Error "funding tx not needed cause channel already created"
            ChannelWrapper.Create
                (NodeId serializedChannel.RemoteNodeId)
                (Account.CreatePayoutScript account)
                transportListener.NodeSecret
                serializedChannel.ChannelIndex
                fundingTxProvider
                serializedChannel.ChanState
        let! peerWrapper =
            let nodeId = channelWrapper.RemoteNodeId
            let peerId = PeerId (serializedChannel.CounterpartyIP :> EndPoint)
            if initiateConnection then
                PeerWrapper.ConnectFromTransportListener
                    transportListener
                    nodeId
                    peerId
            else
                PeerWrapper.AcceptFromTransportListener
                    transportListener
                    nodeId

        let ourReestablishMsgRes, channelWrapper =
            let channelCmd = ChannelCommand.CreateChannelReestablish
            channelWrapper.ExecuteCommand channelCmd <| function
                | (WeSentChannelReestablish(ourReestablishMsg)::[]) ->
                    Some ourReestablishMsg
                | _ -> None
        let ourReestablishMsg = Unwrap ourReestablishMsgRes "error executing channel reestablish command"

        DebugLogger <| SPrintF1 "sending reestablish for %s" (channelId.Value.ToString())
        let! peerWrapper = peerWrapper.SendMsg ourReestablishMsg
        DebugLogger <| SPrintF1 "receiving reestablish for %s" (channelId.Value.ToString())
        let! peerWrapper, _theirReestablishMsg = async {
            let! peerWrapper, channelMsgRes = peerWrapper.RecvChannelMsg()
            let channelMsg =
                match channelMsgRes with
                | Ok channelMsg -> channelMsg
                | Error errorMessage -> raise <| ReestablishChannelError errorMessage
            match channelMsg with
            | :? ChannelReestablish as reestablishMsg -> return peerWrapper, reestablishMsg
            | :? FundingLocked ->
                let! peerWrapper, channelMsgRes = peerWrapper.RecvChannelMsg()
                let channelMsg =
                    match channelMsgRes with
                    | Ok channelMsg -> channelMsg
                    | Error errorMessage -> raise <| ReestablishChannelError errorMessage
                match channelMsg with
                | :? ChannelReestablish as reestablishMsg -> return peerWrapper, reestablishMsg
                | msg -> return raise <| UnexpectedMsg(["ChannelReestablish"], msg)
            | msg -> return raise <| UnexpectedMsg(["ChannelReestablish"; "FundingLocked"], msg)
        }

        // TODO: check their reestablish msg
        //
        // A channel_reestablish message contains the channel ID as well as
        // information specifying what state the remote node thinks the channel
        // is in. So we need to check that the channel IDs match, validate that
        // the information they've sent us makes sense, and possibly re-send
        // commitments. Aside from checking the channel ID this is the sort of
        // thing that should be handled by DNL, except DNL doesn't have an
        // ApplyChannelReestablish command.
        let minimumDepth = serializedChannel.MinSafeDepth
        let channelIndex = serializedChannel.ChannelIndex
        let connectedChannel = {
            Account = account
            ChannelWrapper = channelWrapper
            PeerWrapper = peerWrapper
            MinimumDepth = minimumDepth
            ChannelIndex = channelIndex
        }
        return connectedChannel
    }

    member this.SaveToWallet() =
        let serializedChannel = {
            ChannelIndex = this.ChannelIndex
            Network = this.ChannelWrapper.Network
            RemoteNodeId = this.PeerWrapper.RemoteNodeId.Value
            ChanState = this.ChannelWrapper.Channel.State
            AccountFileName = this.Account.AccountFile.Name
            CounterpartyIP = this.PeerWrapper.RemoteEndPoint
            MinSafeDepth = this.MinimumDepth
        }
        serializedChannel.SaveToWallet()

    member this.RemoteNodeId
        with get(): NodeId = this.ChannelWrapper.RemoteNodeId

    member this.Network
        with get(): Network = this.ChannelWrapper.Network

    member this.ChannelId
        with get(): ChannelId = this.ChannelWrapper.ChannelId

    member this.FundingTxId
        with get(): TxId = this.ChannelWrapper.FundingTxId

    member this.SendError (err: string): Async<ConnectedChannel> = async {
        let errorMsg = {
            ChannelId =
                match this.ChannelWrapper.Channel.State.ChannelId with
                | Some channelId -> WhichChannel.SpecificChannel channelId
                | _ -> WhichChannel.All
            Data = System.Text.Encoding.ASCII.GetBytes err
        }
        let! peerWrapper = this.PeerWrapper.SendMsg errorMsg
        return {
            this with
                PeerWrapper = peerWrapper
        }
    }

