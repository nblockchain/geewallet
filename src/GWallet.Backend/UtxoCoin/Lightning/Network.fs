namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Net.Sockets
open System.Net
open System.Diagnostics
open System.IO

open NBitcoin
open DotNetLightning.Peer
open DotNetLightning.Utils
open ResultUtils.Portability
open NOnion.Network
open NOnion.Directory
open NOnion.Services

open GWallet.Backend
open GWallet.Backend.UtxoCoin
open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks

type PeerDisconnectedError =
    {
        Abruptly: bool
    }
    interface IErrorMsg with
        member self.Message =
            if self.Abruptly then
                "peer disconnected after sending a partial message"
            else
                "peer disconnected"
        member __.ChannelBreakdown =
            false

    member internal self.PossibleBug =
        not self.Abruptly

type HandshakeError =
    | TcpConnect of seq<SocketException>
    | TcpAccept of seq<SocketException>
    | DisconnectedOnAct1 of PeerDisconnectedError
    | InvalidAct1 of PeerError
    | DisconnectedOnAct2 of PeerDisconnectedError
    | InvalidAct2 of PeerError
    | DisconnectedOnAct3 of PeerDisconnectedError
    | InvalidAct3 of PeerError
    interface IErrorMsg with
        member self.Message =
            match self with
            | TcpConnect errs ->
                let messages = Seq.map (fun (err: SocketException) -> err.Message) errs
                SPrintF1 "TCP connection failed: %s" (String.concat "; " messages)
            | TcpAccept errs ->
                let messages = Seq.map (fun (err: SocketException) -> err.Message) errs
                SPrintF1 "TCP accept failed: %s" (String.concat "; " messages)
            | DisconnectedOnAct1 err ->
                SPrintF1 "Peer disconnected before starting handshake: %s" (err :> IErrorMsg).Message
            | InvalidAct1 err ->
                SPrintF1 "Invalid handshake act 1: %s" err.Message
            | DisconnectedOnAct2 err ->
                SPrintF1 "Peer disconnected before sending handshake act 2: %s" (err :> IErrorMsg).Message
            | InvalidAct2 err ->
                SPrintF1 "Invalid handshake act 2: %s" err.Message
            | DisconnectedOnAct3 err ->
                SPrintF1 "Peer disconnected before sending handshake act 3: %s" (err :> IErrorMsg).Message
            | InvalidAct3 err ->
                SPrintF1 "Invalid handshake act 3: %s" err.Message
        member __.ChannelBreakdown =
            false

    member internal self.PossibleBug =
        match self with
        | DisconnectedOnAct1 _
        | DisconnectedOnAct2 _
        | DisconnectedOnAct3 _ -> false
        | TcpConnect _
        | TcpAccept _
        | InvalidAct1 _
        | InvalidAct2 _
        | InvalidAct3 _ -> false

type RecvBytesError =
    | PeerDisconnected of PeerDisconnectedError
    | Decryption of PeerError
    interface IErrorMsg with
        member self.Message =
            match self with
            | PeerDisconnected err ->
                SPrintF1 "Peer disconnected: %s" (err :> IErrorMsg).Message
            | Decryption err ->
                SPrintF1 "Error decrypting message from peer: %s" err.Message
        member self.ChannelBreakdown: bool =
            match self with
            | PeerDisconnected peerDisconnectedError -> (peerDisconnectedError :> IErrorMsg).ChannelBreakdown
            | Decryption _ -> true

    member internal self.PossibleBug =
        match self with
        | PeerDisconnected err -> err.PossibleBug
        | Decryption _ -> false

type IncomingConnectionMethod =
    | Tcp of TcpListener
    | Tor of TorServiceHost

type EndPointType =
    | Tcp of NodeEndPoint
    | Tor of NOnionEndPoint

    override self.ToString() =
        match self with
        | Tcp tcpEndPoint ->
            tcpEndPoint.ToString()
        | Tor torEndPoint ->
            torEndPoint.ToString()

type internal TransportListener =
    internal {
        NodeMasterPrivKey: NodeMasterPrivKey
        Listener: IncomingConnectionMethod
        NodeServerType: NodeServerType
    }
    interface IDisposable with
        member self.Dispose() =
            match self.Listener with
            | IncomingConnectionMethod.Tcp tcp ->
                tcp.Stop()
            | IncomingConnectionMethod.Tor _tor ->
                // TODO: stop the TorServiceHost (how do we do that?)
                ()

    static member internal TcpBind (nodeMasterPrivKey: NodeMasterPrivKey) (endPoint: IPEndPoint) =
        let listener = new TcpListener (endPoint)
        listener.ExclusiveAddressUse <- false
        listener.Server.SetSocketOption(
            SocketOptionLevel.Socket,
            SocketOptionName.ReuseAddress,
            true
        )
        listener.Start()

        {
            NodeMasterPrivKey = nodeMasterPrivKey
            Listener = IncomingConnectionMethod.Tcp listener
            NodeServerType = NodeServerType.Tcp endPoint
        }

    member internal self.LocalIPEndPoint: Option<IPEndPoint> =
        match self.Listener with
        | IncomingConnectionMethod.Tcp tcp ->
            // sillly .NET API: downcast is even done in the sample from the docs: https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.tcplistener.localendpoint?view=netcore-3.1
            Some (tcp.LocalEndpoint :?> IPEndPoint)
        | IncomingConnectionMethod.Tor _tor ->
            // TODO: IPEndPoint of TorServiceHost?
            None

    static member internal TorBind (nodeMasterPrivKey: NodeMasterPrivKey) =
        async {
            let! directory = TorOperations.GetTorDirectory()
            let! host = TorOperations.StartTorServiceHost directory

            return {
                NodeMasterPrivKey = nodeMasterPrivKey
                Listener = IncomingConnectionMethod.Tor host
                NodeServerType = NodeServerType.Tor
            }
        }

    static member internal Bind
        (nodeMasterPrivKey: NodeMasterPrivKey)
        (nodeServerType: NodeServerType) =
        async {
            match nodeServerType with
            | NodeServerType.Tcp bindAddress ->
                return TransportListener.TcpBind nodeMasterPrivKey bindAddress
            | NodeServerType.Tor -> return! TransportListener.TorBind nodeMasterPrivKey
        }

    member internal self.NodeId: NodeId =
        self.NodeMasterPrivKey.NodeId()

    member internal self.PubKey: PubKey =
        self.NodeId.Value

    member internal self.EndPoint: EndPointType =
        let nodeId = PublicKey self.PubKey
        match self.Listener with
        | IncomingConnectionMethod.Tcp tcp ->
            let localIpEndPoint = tcp.LocalEndpoint :?> IPEndPoint
            EndPointType.Tcp (NodeEndPoint.FromParts nodeId localIpEndPoint)
        | IncomingConnectionMethod.Tor tor ->
            EndPointType.Tor ({ NOnionEndPoint.NodeId = nodeId; Url = tor.ExportUrl() })


type PeerErrorMessage =
    {
        ErrorMsg: DotNetLightning.Serialization.Msgs.ErrorMsg
    }
    interface IErrorMsg with
        member self.Message =
            if self.ErrorMsg.Data.Length = 1 then
                let code = self.ErrorMsg.Data.[0]
                (SPrintF1 "Error code %i received from lightning peer: " code) +
                match code with
                | 0x01uy ->
                    "The number of pending channels exceeds the policy limit.\n\
                    Hint: You can try from a new node identity."
                | 0x02uy ->
                    "Node is not synced to blockchain." +
                    if Config.BitcoinNet() = Network.RegTest then
                        "\nHint: Try mining some blocks before opening."
                    else
                        String.Empty
                | 0x03uy ->
                    "Channel capacity too large.\n\
                    Hint: Try with a smaller funding amount."
                | _ ->
                    "(unknown error code)"
            else
                System.Text.ASCIIEncoding.ASCII.GetString self.ErrorMsg.Data

        member __.ChannelBreakdown: bool =
            true

type StreamType =
    | TcpNetworkStream of NetworkStream
    | TorClientStream of TorStream

    member self.Write (data: array<byte>) =
        async {
            match self with
            | StreamType.TorClientStream torStream ->
                do! torStream.SendData data
            | StreamType.TcpNetworkStream tcpStream ->
                do! tcpStream.WriteAsync(data, 0, data.Length)
                    |> Async.AwaitTask
        }

type TransportClientType =
    | TcpClient of TcpClient
    | TorClient of TorServiceClient

    member self.GetStream() =
        match self with
        | TransportClientType.TcpClient tcpClient ->
            tcpClient.GetStream()
            |> StreamType.TcpNetworkStream
        | TransportClientType.TorClient torClient ->
            torClient.GetStream()
            |> StreamType.TorClientStream


type TransportType =
    | Tcp of TcpClient
    | Tor of TorStream

    member self.GetStream() =
        match self with
        | TransportType.Tcp tcp ->
            tcp.GetStream()
            |> StreamType.TcpNetworkStream
        | TransportType.Tor tor ->
            tor |> StreamType.TorClientStream

type internal TransportStream =
    internal {
        NodeMasterPrivKey: NodeMasterPrivKey
        Peer: Peer
        Client: TransportType
    }
    interface IDisposable with
        member self.Dispose() =
            match self.Client with
            | TransportType.Tcp tcpClient ->
                tcpClient.Close()
            | TransportType.Tor _torStream ->
                // FIXME: TorServiceClient needs to be disposed.
                ()

    static member private bolt08EncryptedMessageLengthPrefixLength = 18
    static member private bolt08EncryptedMessageMacLength = 16
    // https://github.com/lightningnetwork/lightning-rfc/blob/master/08-transport.md#authenticated-key-exchange-handshake-specification
    static member private bolt08ActOneLength = 50
    static member private bolt08ActTwoLength = 50
    static member private bolt08ActThreeLength = 66

    static member private ReadExactAsync (stream: StreamType)
                                         (numberBytesToRead: int)
                                             : Async<Result<array<byte>, PeerDisconnectedError>> =
        let buf: array<byte> = Array.zeroCreate numberBytesToRead
        let rec read buf totalBytesRead =
            let readAsync () =
                async {
                    match stream with
                    | StreamType.TcpNetworkStream tcpStream ->
                        return! tcpStream.ReadAsync(buf, totalBytesRead, numberBytesToRead - totalBytesRead) |> Async.AwaitTask
                    | StreamType.TorClientStream torStream ->
                        return! torStream.Receive buf totalBytesRead (numberBytesToRead - totalBytesRead)
                }
            async {
                let! maybeBytesRead =
                    async {
                        try
                            let! res = readAsync ()
                            return Some res
                        with
                        | ex ->
                            if (FSharpUtil.FindException<System.Net.Sockets.SocketException> ex).IsSome then
                                return None
                            else
                                return raise <| FSharpUtil.ReRaise ex
                    }

                match maybeBytesRead with
                | Some bytesRead ->
                    let totalBytesRead = totalBytesRead + bytesRead
                    if bytesRead = 0 then
                        if totalBytesRead = 0 then
                            return Error { Abruptly = false }
                        else
                            return Error { Abruptly = true }
                    else
                        if totalBytesRead < numberBytesToRead then
                            return! read buf totalBytesRead
                        else
                            return Ok buf
                | None -> return Error { Abruptly = true }
            }
        read buf 0

    static member private TcpTransportConnect
        (localEndPointOpt: Option<IPEndPoint>)
        (remoteEndPoint: IPEndPoint)
        : Async<Result<TcpClient, seq<SocketException>>> = async {
        let client = new TcpClient (remoteEndPoint.AddressFamily)
        match localEndPointOpt with
        | Some localEndPoint ->
            client.Client.ExclusiveAddressUse <- false
            client.Client.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true
            )
            client.Client.Bind(localEndPoint)
            Infrastructure.LogDebug <| SPrintF2 "Connecting over TCP from %A to %A..." localEndPoint remoteEndPoint
        | None ->
            Infrastructure.LogDebug <| SPrintF1 "Connecting over TCP to %A..." remoteEndPoint
        try
            do! client.ConnectAsync(remoteEndPoint.Address, remoteEndPoint.Port) |> Async.AwaitTask
            return Ok client
        with
        | ex ->
            client.Close()
            let socketExceptions = FindSingleException<SocketException> ex
            return Error socketExceptions
        }

    static member private AcceptAny (listener: IncomingConnectionMethod)
        : Async<Result<TransportType, seq<SocketException>>> = async {
        try
            match listener with
            | IncomingConnectionMethod.Tcp tcpListener ->
                let! client = tcpListener.AcceptTcpClientAsync() |> Async.AwaitTask
                return Ok (TransportType.Tcp client)
            | IncomingConnectionMethod.Tor torListener ->
                let! client = torListener.AcceptClient()
                return Ok (TransportType.Tor client)
        with
        | ex ->
            let socketExceptions = FindSingleException<SocketException> ex
            return Error socketExceptions
    }

    static member private ConnectHandshake (client: TransportClientType)
                                           (nodeMasterPrivKey: NodeMasterPrivKey)
                                           (peerNodeId: NodeId)
                                               : Async<Result<TransportStream, HandshakeError>> = async {
        let nodeSecret = nodeMasterPrivKey.NodeSecret()
        let stream = client.GetStream()
        // FIXME: CreateOutbound should take a NodeSecret
        let peer = Peer.CreateOutbound(peerNodeId, nodeSecret.RawKey())
        let act1, peerEncryptor = PeerChannelEncryptor.getActOne peer.ChannelEncryptor
        Debug.Assert((TransportStream.bolt08ActOneLength = act1.Length), "act1 has wrong length")
        let peerAfterAct1 = { peer with ChannelEncryptor = peerEncryptor }

        // Send act1
        do! stream.Write act1

        // Receive act2
        Infrastructure.LogDebug "Receiving Act 2..."
        let! act2Res = TransportStream.ReadExactAsync stream TransportStream.bolt08ActTwoLength
        match act2Res with
        | Error peerDisconnectedError -> return Error <| DisconnectedOnAct2 peerDisconnectedError
        | Ok act2 ->
            let peerCmd = ProcessActTwo(act2, nodeSecret.RawKey())
            match Peer.executeCommand peerAfterAct1 peerCmd with
            | Error err -> return Error <| InvalidAct2 err
            | Ok (ActTwoProcessed ((act3, _), _) as evt::[]) ->
                let peerAfterAct2 = Peer.applyEvent peerAfterAct1 evt

                Debug.Assert((TransportStream.bolt08ActThreeLength = act3.Length), SPrintF1 "act3 has wrong length (not %i)" TransportStream.bolt08ActThreeLength)

                do! stream.Write act3

                return Ok {
                    NodeMasterPrivKey = nodeMasterPrivKey
                    Peer = peerAfterAct2
                    Client =
                        match client with
                        | TransportClientType.TcpClient tcpClient ->
                            tcpClient |> TransportType.Tcp
                        | TransportClientType.TorClient torClient ->
                            torClient.GetStream() |> TransportType.Tor
                }
            | Ok evts ->
                return failwith <| SPrintF1
                    "DNL returned unexpected events when processing act2: %A" evts
    }

    static member private TorTransportConnect
        (nonionEndPoint: NOnionEndPoint)
        : Async<Result<TorServiceClient, seq<SocketException>>> =
        async {
            let! directory = TorOperations.GetTorDirectory()
            try
                let! torClient = TorOperations.TorConnect directory nonionEndPoint.Url
                Infrastructure.LogDebug <| SPrintF1 "Connected %s" nonionEndPoint.Url
                return Ok torClient
            with
            | ex ->
                let socketExceptions = FindSingleException<SocketException> ex
                return Error socketExceptions
        }

    static member private TransportConnect
        (localEndPointOpt: Option<IPEndPoint>)
        (node: NodeIdentifier)
        : Async<Result<TransportClientType, seq<SocketException>>> =
        async {
            match node with
            | NodeIdentifier.TcpEndPoint remoteEndPoint ->
                let! maybeTcpClient = TransportStream.TcpTransportConnect localEndPointOpt remoteEndPoint.IPEndPoint
                match maybeTcpClient with
                | Ok tcpClient ->
                    return Ok (TransportClientType.TcpClient tcpClient)
                | Error error ->
                    return Error error
            | NodeIdentifier.TorEndPoint nonionIntroductionPoint ->
                let! maybeTorClient = TransportStream.TorTransportConnect nonionIntroductionPoint
                match maybeTorClient with
                | Ok torClient ->
                    return Ok (TransportClientType.TorClient torClient)
                | Error error ->
                    return Error error
        }

    static member internal Connect
        (nodeMasterPrivKey: NodeMasterPrivKey)
        (nodeIdentifier: NodeIdentifier)
        : Async<Result<TransportStream, HandshakeError>> = async {
        let nodeId = nodeIdentifier.NodeId
        let! connectRes = TransportStream.TransportConnect None nodeIdentifier
        match connectRes with
        | Error err -> return Error <| TcpConnect err
        | Ok client ->
            return! TransportStream.ConnectHandshake client nodeMasterPrivKey nodeId
    }

    static member internal AcceptFromTransportListener (transportListener: TransportListener)
                                                           : Async<Result<TransportStream, HandshakeError>> = async {
        let! clientRes = TransportStream.AcceptAny transportListener.Listener
        match clientRes with
        | Error socketError -> return Error <| TcpAccept socketError
        | Ok client ->
            let nodeSecret = transportListener.NodeMasterPrivKey.NodeSecret()
            let nodeSecretKey = nodeSecret.RawKey()
            let stream = client.GetStream()
            let peer = Peer.CreateInbound(nodeSecretKey)
            let! act1Res = TransportStream.ReadExactAsync stream TransportStream.bolt08ActOneLength
            match act1Res with
            | Error peerDisconnectedError -> return Error <| DisconnectedOnAct1 peerDisconnectedError
            | Ok act1 ->
                let peerCmd = ProcessActOne(act1, nodeSecretKey)
                match Peer.executeCommand peer peerCmd with
                | Error err -> return Error <| InvalidAct1 err
                | Ok (ActOneProcessed(act2, _) as evt::[]) ->
                    let peerAfterAct2 = Peer.applyEvent peer evt
                    do! stream.Write act2
                    let! act3Res = TransportStream.ReadExactAsync stream TransportStream.bolt08ActThreeLength
                    match act3Res with
                    | Error peerDisconnectedError ->
                        return Error <| DisconnectedOnAct3 peerDisconnectedError
                    | Ok act3 ->
                        let peerCmd = ProcessActThree act3
                        match Peer.executeCommand peerAfterAct2 peerCmd with
                        | Error err -> return Error <| InvalidAct3 err
                        | Ok (ActThreeProcessed(_, _) as evt::[]) ->
                            let peerAfterAct3 = Peer.applyEvent peerAfterAct2 evt

                            return Ok {
                                NodeMasterPrivKey = transportListener.NodeMasterPrivKey
                                Peer = peerAfterAct3
                                Client = client
                            }
                        | Ok evts ->
                            return failwith <| SPrintF1
                                "DNL returned unexpected events when processing act3: %A" evts
                | Ok evts ->
                    return failwith <| SPrintF1
                        "DNL returned unexpected events when processing act1: %A" evts
    }

    member internal self.RemoteNodeId
        with get(): NodeId =
            match self.Peer.TheirNodeId with
            | Some nodeId ->
                nodeId
            | None -> 
                failwith
                    "The TransportStream type is created by performing a handshake \
                    and in doing so guarantees that the peer's node id is known"

    member internal self.RemoteEndPoint
        with get(): Option<IPEndPoint> =
            match self.Client with
            | TransportType.Tcp tcpClient ->
                Some (tcpClient.Client.RemoteEndPoint :?> IPEndPoint)
            | TransportType.Tor _torStream ->
                None

    member internal self.NodeEndPoint: Option<NodeEndPoint> =
        match self.RemoteEndPoint with
        | Some remoteEndPoint ->
            let remoteNodeId = PublicKey self.RemoteNodeId.Value
            Some (NodeEndPoint.FromParts remoteNodeId remoteEndPoint)
        | _ -> None

    member internal self.SendBytes (plaintext: array<byte>): Async<TransportStream> = async {
        let peer = self.Peer
        let ciphertext, channelEncryptor =
            PeerChannelEncryptor.encryptMessage plaintext peer.ChannelEncryptor
        let peerAfterBytesSent = { peer with ChannelEncryptor = channelEncryptor }
        match self.Client with
        | TransportType.Tcp tcpClient ->
            let stream = tcpClient.GetStream()
            do! stream.WriteAsync(ciphertext, 0, ciphertext.Length) |> Async.AwaitTask
        | TransportType.Tor stream ->
            do! stream.SendData ciphertext
        return { self with Peer = peerAfterBytesSent }
    }

    member internal self.RecvBytes(): Async<Result<TransportStream * array<byte>, RecvBytesError>> = async {
        let peer = self.Peer
        let stream =
            match self.Client with
            | TransportType.Tcp tcpClient ->
                StreamType.TcpNetworkStream (tcpClient.GetStream())
            | TransportType.Tor stream ->
                StreamType.TorClientStream stream

        let! encryptedLengthRes =
            TransportStream.ReadExactAsync stream TransportStream.bolt08EncryptedMessageLengthPrefixLength
        match encryptedLengthRes with
        | Error peerDisconnectedError -> return Error <| PeerDisconnected peerDisconnectedError
        | Ok encryptedLength ->
            let decryptLengthRes =
                PeerChannelEncryptor.decryptLengthHeader encryptedLength peer.ChannelEncryptor
            match decryptLengthRes with
            | Error peerError -> return Error <| Decryption peerError
            | Ok (length, channelEncryptor) ->
                let! ciphertextRes =
                    TransportStream.ReadExactAsync
                        stream
                        (int length + TransportStream.bolt08EncryptedMessageMacLength)
                match ciphertextRes with
                | Error _peerDisconnectedError -> return Error <| PeerDisconnected { Abruptly = true }
                | Ok ciphertext ->
                    let decryptBodyRes =
                        PeerChannelEncryptor.decryptMessage ciphertext channelEncryptor
                    match decryptBodyRes with
                    | Error peerError -> return Error <| Decryption peerError
                    | Ok (plaintext, channelEncryptor) ->
                        let peerAfterBytesReceived = { peer with ChannelEncryptor = channelEncryptor }
                        let transportStream = { self with Peer = peerAfterBytesReceived }
                        return Ok (transportStream, plaintext)
    }

