namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Net
open System.Net.Sockets

open NBitcoin

open DotNetLightning.Utils
open DotNetLightning.Serialize.Msgs

open GWallet.Backend
open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks
open FSharp.Core

type PeerErrorMessage = {
    ErrorMessage: ErrorMessage
} with
    override this.ToString() =
        if this.ErrorMessage.Data.Length = 1 then
            let code = this.ErrorMessage.Data.[0]
            (SPrintF1 "Error code %i received from lightning peer: " code) +
            match code with
            | 0x01uy ->
                "The number of pending channels exceeds the policy limit.\n\
                Hint: You can try from a new node identity."
            | 0x02uy ->
                "Node is not synced to blockchain." +
                if Config.BitcoinNet = Network.RegTest then
                    "\nHint: Try mining some blocks before opening."
                else
                    String.Empty
            | 0x03uy ->
                "Channel capacity too large.\n\
                Hint: Try with a smaller funding amount."
            | _ ->
                "(unknown error code)"
        else
            System.Text.ASCIIEncoding.ASCII.GetString this.ErrorMessage.Data

type PeerWrapper = {
    Init: Init
    MsgStream: MsgStream
} with
    interface IDisposable with
        member this.Dispose() =
            (this.MsgStream :> IDisposable).Dispose()

    static member ConnectFromTransportListener (transportListener: TransportListener)
                                               (peerNodeId: NodeId)
                                               (peerId: PeerId)
                                                   : Async<PeerWrapper> =
        let rec accept() = async {
            let! init, msgStream =
                MsgStream.AcceptFromTransportListener transportListener
            if msgStream.RemoteNodeId = peerNodeId && msgStream.PeerId = peerId then
                return {
                    Init = init
                    MsgStream = msgStream
                }
            else
                return! accept()
        }

        let connect = async {
            let! init, msgStream =
                let rec reconnect (backoff: TimeSpan) = async {
                    try
                        return!
                            MsgStream.ConnectFromTransportListener
                                transportListener
                                peerNodeId
                                peerId
                    with
                    | ex ->
                        match FSharpUtil.FindException<SocketException> ex with
                        | None -> return raise <| FSharpUtil.ReRaise ex
                        | Some socketEx ->
                            DebugLogger <| SPrintF1 "got socket exception: %s" (socketEx.ToString())
                            Console.WriteLine(SPrintF1 "connection refused. trying again in %f seconds" backoff.TotalSeconds)
                            do! Async.Sleep (int backoff.TotalMilliseconds)
                            return! reconnect (backoff + backoff)
                }
                reconnect(TimeSpan.FromSeconds(1.0))
            return {
                Init = init
                MsgStream = msgStream
            }
        }

        AsyncExtensions.WhenAny [accept(); connect]

    static member AcceptFromTransportListener (transportListener: TransportListener)
                                                  : Async<PeerWrapper> = async {
        let! init, msgStream = MsgStream.AcceptFromTransportListener transportListener
        return {
            Init = init
            MsgStream = msgStream
        }
    }

    member this.RemoteNodeId
        with get(): NodeId = this.MsgStream.RemoteNodeId

    member this.PeerId
        with get(): PeerId = this.MsgStream.PeerId

    member this.RemoteEndPoint
        with get(): IPEndPoint = this.MsgStream.RemoteEndPoint

    member this.NodeSecret
        with get(): ExtKey = this.MsgStream.NodeSecret

    member this.SendMsg (msg: ILightningMsg): Async<PeerWrapper> = async {
        let! msgStream = this.MsgStream.SendMsg msg
        return { this with MsgStream = msgStream }
    }

    member this.RecvChannelMsg(): Async<PeerWrapper * Result<IChannelMsg, PeerErrorMessage>> =
        let rec recv (msgStream: MsgStream) = async {
            let! msgStream, msg = msgStream.RecvMsg()
            match msg with
            | :? ErrorMessage as errorMessage ->
                let peerWrapper = { this with MsgStream = msgStream }
                return peerWrapper, Error { ErrorMessage = errorMessage }
            | :? Ping as pingMsg ->
                let! msgStream = msgStream.SendMsg { Pong.BytesLen = pingMsg.PongLen }
                return! recv msgStream
            | :? Pong ->
                return failwith "sending pings is not implemented"
            | :? Init ->
                return failwith "unexpected init msg"
            | :? IRoutingMsg ->
                DebugLogger "handling routing messages is not implemented"
                return! recv msgStream
            | :? IChannelMsg as msg ->
                let peerWrapper = { this with MsgStream = msgStream }
                return peerWrapper, Ok msg
            | _ ->
                return failwith <| SPrintF1 "unreachable %A" msg
        }
        recv this.MsgStream


