namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Net

open NBitcoin
open DotNetLightning.Serialization
open DotNetLightning.Serialization.Msgs
open DotNetLightning.Utils
open ResultUtils.Portability

open GWallet.Backend
open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks

type InitializeError =
    | ReceiveInit of RecvBytesError
    | DeserializeInit of P2PDecodeError
    | UnexpectedMsg of ILightningMsg
    interface IErrorMsg with
        member self.Message =
            match self with
            | ReceiveInit err ->
                SPrintF1 "Error receiving init message: %s" (err :> IErrorMsg).Message
            | DeserializeInit err ->
                SPrintF1 "Error deserializing init message: %s" err.Message
            | UnexpectedMsg msg ->
                SPrintF1 "Expected init message, got %A" (msg.GetType())
        member self.ChannelBreakdown: bool =
            match self with
            | ReceiveInit recvBytesError ->
                (recvBytesError :> IErrorMsg).ChannelBreakdown
            | DeserializeInit _ -> true
            | UnexpectedMsg _ -> false

    member internal self.PossibleBug =
        match self with
        | ReceiveInit err -> err.PossibleBug
        | DeserializeInit _
        | UnexpectedMsg _ -> false

type ConnectError =
    | Handshake of HandshakeError
    | Initialize of InitializeError
    interface IErrorMsg with
        member self.Message =
            match self with
            | Handshake err ->
                SPrintF1 "Handshake failed: %s" (err :> IErrorMsg).Message
            | Initialize err ->
                SPrintF1 "Message stream initialization failed: %s" (err :> IErrorMsg).Message
        member self.ChannelBreakdown: bool =
            match self with
            | Handshake handshakeError ->
                (handshakeError :> IErrorMsg).ChannelBreakdown
            | Initialize initializeError ->
                (initializeError :> IErrorMsg).ChannelBreakdown

    member internal self.PossibleBug =
        match self with
        | Handshake err -> err.PossibleBug
        | Initialize err -> err.PossibleBug

type RecvMsgError =
    | RecvBytes of RecvBytesError
    | DeserializeMsg of P2PDecodeError
    interface IErrorMsg with
        member self.Message =
            match self with
            | RecvBytes err ->
                SPrintF1 "Error receiving raw data from peer: %s" (err :> IErrorMsg).Message
            | DeserializeMsg err ->
                SPrintF1 "Error deserializing message from peer: %s" err.Message
        member self.ChannelBreakdown: bool =
            match self with
            | RecvBytes recvBytesError ->
                (recvBytesError :> IErrorMsg).ChannelBreakdown
            | DeserializeMsg _ -> true

    member internal self.PossibleBug =
        match self with
        | RecvBytes err -> err.PossibleBug
        | DeserializeMsg _ -> false

type internal MsgStream =
    {
        TransportStream: TransportStream
    }
    interface IDisposable with
        member self.Dispose() =
            (self.TransportStream :> IDisposable).Dispose()

    static member private InitializeTransportStream (transportStream: TransportStream)
                                                    (currency: Currency)
                                                    (fundingAmountOpt: Option<Money>)
                                                        : Async<Result<InitMsg * MsgStream, InitializeError>> = async {
        let! transportStreamAfterInitSent =
            let plainInit: InitMsg = {
                Features = Settings.SupportedFeatures currency fundingAmountOpt
                TLVStream = [||]
            }
            let msg = plainInit :> ILightningMsg
            let bytes = msg.ToBytes()
            transportStream.SendBytes bytes

        let! transportStreamAfterInitReceivedRes = transportStreamAfterInitSent.RecvBytes()
        match transportStreamAfterInitReceivedRes with
        | Error recvBytesError -> return Error <| ReceiveInit recvBytesError
        | Ok (transportStreamAfterInitReceived, bytes) ->
            match LightningMsg.fromBytes bytes with
            | Error msgError -> return Error <| DeserializeInit msgError
            | Ok msg ->
                match msg with
                | :? InitMsg as initMsg ->
                    let msgStream = { TransportStream = transportStreamAfterInitReceived }
                    Infrastructure.LogDebug <| SPrintF1 "peer init features == %s" (initMsg.Features.PrettyPrint)
                    return Ok (initMsg, msgStream)
                | _ -> return Error <| UnexpectedMsg msg
    }

    static member internal Connect (nodeMasterPrivKey: NodeMasterPrivKey)
                                   (nodeIdentifier: NodeIdentifier)
                                   (currency: Currency)
                                   (fundingAmount: Money)
                                       : Async<Result<InitMsg * MsgStream, ConnectError>> = async {
        let! transportStreamRes =
            TransportStream.Connect
                nodeMasterPrivKey
                nodeIdentifier
        match transportStreamRes with
        | Error handshakeError -> return Error <| Handshake handshakeError
        | Ok transportStream -> 
            let! initializeRes = MsgStream.InitializeTransportStream transportStream currency (Some fundingAmount)
            match initializeRes with
            | Error initializeError -> return Error <| Initialize initializeError
            | Ok (initMsg, msgStream) -> return Ok (initMsg, msgStream)
    }

    static member internal AcceptFromTransportListener (transportListener: TransportListener)
                                                       (currency: Currency)
                                                       (fundingAmountOpt: Option<Money>)
                                                           : Async<Result<InitMsg * MsgStream, ConnectError>> = async {
        let! transportStreamRes =
            TransportStream.AcceptFromTransportListener transportListener
        match transportStreamRes with
        | Error handshakeError -> return Error <| Handshake handshakeError
        | Ok transportStream ->
            let! initializeRes = MsgStream.InitializeTransportStream transportStream currency fundingAmountOpt
            match initializeRes with
            | Error initializeError -> return Error <| Initialize initializeError
            | Ok (initMsg, msgStream) -> return Ok (initMsg, msgStream)
    }

    member internal self.RemoteNodeId
        with get(): NodeId = self.TransportStream.RemoteNodeId

    member internal self.RemoteEndPoint
        with get(): Option<IPEndPoint> = self.TransportStream.RemoteEndPoint

    member internal self.NodeEndPoint: Option<NodeEndPoint> =
        self.TransportStream.NodeEndPoint

    member internal self.NodeMasterPrivKey(): NodeMasterPrivKey =
        self.TransportStream.NodeMasterPrivKey

    member internal self.SendMsg (msg: ILightningMsg): Async<MsgStream> = async {
        let bytes = msg.ToBytes()
        let! transportStream = self.TransportStream.SendBytes bytes
        return { self with TransportStream = transportStream }
    }

    member internal self.RecvMsg(): Async<Result<MsgStream * ILightningMsg, RecvMsgError>> = async {
        Console.WriteLine(SPrintF2 "*** line %s of %s" __LINE__ __SOURCE_FILE__)
        let! recvBytesRes = self.TransportStream.RecvBytes()
        Console.WriteLine(SPrintF2 "*** line %s of %s" __LINE__ __SOURCE_FILE__)
        match recvBytesRes with
        | Error recvBytesError -> return Error <| RecvBytes recvBytesError
        | Ok (transportStream, bytes) ->
            match LightningMsg.fromBytes bytes with
            | Error msgError -> return Error <| DeserializeMsg msgError
            | Ok msg ->
                return Ok ({ self with TransportStream = transportStream }, msg)
    }

