namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Collections
open System.Net
open NBitcoin

open DotNetLightning.Utils
open DotNetLightning.Serialize.Msgs
open DotNetLightning.Serialize

open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks
open GWallet.Backend.UtxoCoin.Lightning.Util

type UnexpectedMsg(expected: List<string>, got: ILightningMsg) =
    inherit Exception(
        let expectedMsg =
            if expected.Length = 1 then
                expected.Head
            else
                List.fold
                    (fun acc item -> SPrintF2 "%s, %s" acc item)
                    (SPrintF1 "one of %s" expected.Head)
                    expected.Tail
        let gotMsg = got.GetType().ToString()
        SPrintF3
            "Unexpected lightning message. Expected %s. Got %s. Full message: %A"
            expectedMsg
            gotMsg
            got
    )

type DeserializationException(err: P2PDecodeError) =
    inherit Exception(SPrintF1 "deserialization error: %s" (err.ToString()))

type MsgStream = {
    TransportStream: TransportStream
} with
    interface IDisposable with
        member this.Dispose() =
            (this.TransportStream :> IDisposable).Dispose()

    static member SupportedFeatures: FeatureBit = 
        let maxBitPosition =
            Feature.OptionDataLossProtect.MandatoryBitPosition
        let mutable bitArray = BitArray(1 + maxBitPosition)
        bitArray.[Feature.OptionDataLossProtect.MandatoryBitPosition] <- true
        Unwrap (FeatureBit.TryCreate(bitArray)) "our feature flags constant is invalid"

    static member private InitializeTransportStream (transportStream: TransportStream)
                                                        : Async<Init * MsgStream> = async {
        let! transportStream =
            let plainInit: Init = {
                Features = MsgStream.SupportedFeatures
                TLVStream = [||]
            }
            let msg = plainInit :> ILightningMsg
            let bytes = msg.ToBytes()
            transportStream.SendBytes bytes

        let! transportStream, bytes = transportStream.RecvBytes()
        let msg =
            match LightningMsg.fromBytes bytes with
            | Ok msg -> msg
            | Error msgError -> raise <| DeserializationException msgError
        let init =
            match msg with
            | :? Init as init -> init
            | msg -> raise <| UnexpectedMsg(["Init"], msg)

        let msgStream = { TransportStream = transportStream }
        DebugLogger <| SPrintF1 "peer init features == %s" (init.Features.PrettyPrint)
        return init, msgStream
    }

    static member ConnectFromTransportListener (transportListener: TransportListener)
                                               (peerNodeId: NodeId)
                                               (peerId: PeerId)
                                                   : Async<Init * MsgStream> = async {
        let! transportStream =
            TransportStream.ConnectFromTransportListener
                transportListener
                peerNodeId
                peerId
        return! MsgStream.InitializeTransportStream transportStream
    }

    static member AcceptFromTransportListener (transportListener: TransportListener)
                                                  : Async<Init * MsgStream> = async {
        let! transportStream =
            TransportStream.AcceptFromTransportListener transportListener
        return! MsgStream.InitializeTransportStream transportStream
    }

    member this.RemoteNodeId
        with get(): NodeId = this.TransportStream.RemoteNodeId

    member this.PeerId
        with get(): PeerId = this.TransportStream.PeerId

    member this.RemoteEndPoint
        with get(): IPEndPoint = this.TransportStream.RemoteEndPoint

    member this.NodeSecret
        with get(): ExtKey = this.TransportStream.NodeSecret

    member this.SendMsg (msg: ILightningMsg): Async<MsgStream> = async {
        let bytes = msg.ToBytes()
        let! transportStream = this.TransportStream.SendBytes bytes
        return { this with TransportStream = transportStream }
    }

    member this.RecvMsg(): Async<MsgStream * ILightningMsg> = async {
        let! transportStream, bytes = this.TransportStream.RecvBytes()
        let msg =
            match LightningMsg.fromBytes bytes with
            | Ok msg -> msg
            | Error msgError -> raise <| DeserializationException msgError
        return { this with TransportStream = transportStream }, msg
    }

