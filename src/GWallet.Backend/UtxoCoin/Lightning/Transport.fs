namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Net.Sockets
open System.Net
open System.Diagnostics

open NBitcoin

open DotNetLightning.Utils
open DotNetLightning.Peer

open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks

type PeerDisconnectedException() =
    inherit Exception("peer disconnected")

type PeerDisconnectedUngracefullyException() =
    inherit Exception("peer disconnected after sending a partial message")

type DecryptionException(peerError: PeerError) =
    inherit Exception(SPrintF1 "error decrypting message from peer: %s" (peerError.ToString()))

type HandshakeException(msg: string) =
    inherit Exception(SPrintF1 "handshake error: %s" msg)

type TransportListener = {
    NodeSecret: ExtKey
    Listener: TcpListener
} with
    interface IDisposable with
        member this.Dispose() =
            this.Listener.Stop()

    static member Bind (nodeSecret: ExtKey) (endpoint: IPEndPoint) =
        let listener = new TcpListener (endpoint)
        listener.ExclusiveAddressUse <- false
        listener.Server.SetSocketOption(
            SocketOptionLevel.Socket,
            SocketOptionName.ReuseAddress,
            true
        )
        listener.Start()
        {
            NodeSecret = nodeSecret
            Listener = listener
        }

    static member BindFromConfig(nodeSecret: ExtKey) =
        let lightningConfig = LightningConfig.Load()
        TransportListener.Bind nodeSecret lightningConfig.LocalEndpoint

    member this.PublicKey
        with get(): PubKey = this.NodeSecret.PrivateKey.PubKey

    member this.LocalEndpoint
        with get(): IPEndPoint = this.Listener.LocalEndpoint :?> IPEndPoint

type TransportStream = {
    NodeSecret: ExtKey
    Peer: Peer
    Client: TcpClient
} with
    interface IDisposable with
        member this.Dispose() =
            this.Client.Close()

    static member bolt08EncryptedMessageLengthPrefixLength = 18
    static member bolt08EncryptedMessageMacLength = 16
    // https://github.com/lightningnetwork/lightning-rfc/blob/master/08-transport.md#authenticated-key-exchange-handshake-specification
    static member bolt08ActOneLength = 50
    static member bolt08ActTwoLength = 50
    static member bolt08ActThreeLength = 66

    static member private ReadExactAsync (stream: NetworkStream)
                                         (numberBytesToRead: int)
                                             : Async<array<byte>> =
        let buf: array<byte> = Array.zeroCreate numberBytesToRead
        let rec read buf totalBytesRead = async {
            let! bytesRead =
                stream.ReadAsync(buf, totalBytesRead, (numberBytesToRead - totalBytesRead))
                |> Async.AwaitTask
            let totalBytesRead = totalBytesRead + bytesRead
            if bytesRead = 0 then
                if totalBytesRead = 0 then
                    return raise <| PeerDisconnectedException()
                else
                    return raise <| PeerDisconnectedUngracefullyException()
            else
                if totalBytesRead < numberBytesToRead then
                    return! read buf totalBytesRead
                else
                    return buf
        }
        read buf 0

    static member ConnectFromTransportListener (transportListener: TransportListener)
                                               (peerNodeId: NodeId)
                                               (peerId: PeerId)
                                                   : Async<TransportStream> = async {

        let nodeSecretKey = transportListener.NodeSecret.PrivateKey
        let peerEndpoint = peerId.Value :?> IPEndPoint
        let client = new TcpClient (peerId.Value.AddressFamily)
        client.Client.ExclusiveAddressUse <- false
        client.Client.SetSocketOption(
            SocketOptionLevel.Socket,
            SocketOptionName.ReuseAddress,
            true
        )
        client.Client.Bind(transportListener.LocalEndpoint)
        Console.WriteLine(SPrintF1 "Connecting over TCP to %A..." peerEndpoint)
        do! client.ConnectAsync(peerEndpoint.Address, peerEndpoint.Port) |> Async.AwaitTask
        let stream = client.GetStream()

        let peer = Peer.CreateOutbound(peerId, peerNodeId, nodeSecretKey)
        let act1, peerEncryptor = PeerChannelEncryptor.getActOne peer.ChannelEncryptor
        Debug.Assert((TransportStream.bolt08ActOneLength = act1.Length), "act1 has wrong length")
        let peer = { peer with ChannelEncryptor = peerEncryptor }

        // Send act1
        do! stream.WriteAsync(act1, 0, act1.Length) |> Async.AwaitTask

        // Receive act2
        DebugLogger "Receiving Act 2..."
        let! act2 = TransportStream.ReadExactAsync stream TransportStream.bolt08ActTwoLength
        let act3, peer =
            let peerCmd = ProcessActTwo(act2, nodeSecretKey)
            match Peer.executeCommand peer peerCmd with
            | Ok (ActTwoProcessed ((act3, _), _) as evt::[]) ->
                let peer = Peer.applyEvent peer evt
                act3, peer
            | _ -> raise <| HandshakeException "Received invalid act2"

        Debug.Assert((TransportStream.bolt08ActThreeLength = act3.Length), SPrintF1 "act3 has wrong length (not %i)" TransportStream.bolt08ActThreeLength)

        do! stream.WriteAsync(act3, 0, act3.Length) |> Async.AwaitTask
        return {
            NodeSecret = transportListener.NodeSecret
            Peer = peer
            Client = client
        }
    }

    static member AcceptFromTransportListener (transportListener: TransportListener)
                                                  : Async<TransportStream> = async {
        let! client = transportListener.Listener.AcceptTcpClientAsync() |> Async.AwaitTask
        let nodeSecretKey = transportListener.NodeSecret.PrivateKey
        let stream = client.GetStream()
        let peerId = client.Client.RemoteEndPoint |> PeerId
        let peer = Peer.CreateInbound(peerId, nodeSecretKey)
        let! act1 = TransportStream.ReadExactAsync stream TransportStream.bolt08ActOneLength
        let act2, peer =
            let peerCmd = ProcessActOne(act1, nodeSecretKey)
            match Peer.executeCommand peer peerCmd with
            | Ok (ActOneProcessed(act2, _) as evt::[]) ->
                let peer = Peer.applyEvent peer evt
                act2, peer
            | _ -> raise <| HandshakeException "Received invalid act1"

        do! stream.WriteAsync(act2, 0, act2.Length) |> Async.AwaitTask
        let! act3 = TransportStream.ReadExactAsync stream TransportStream.bolt08ActThreeLength
        let peer =
            let peerCmd = ProcessActThree act3
            match Peer.executeCommand peer peerCmd with
            | Ok (ActThreeProcessed(_, _) as evt::[]) ->
                Peer.applyEvent peer evt
            | _ -> raise <| HandshakeException "Received invalid act3"

        return {
            NodeSecret = transportListener.NodeSecret
            Peer = peer
            Client = client
        }
    }

    member this.RemoteNodeId
        with get(): NodeId = this.Peer.TheirNodeId.Value

    member this.PeerId
        with get(): PeerId = this.Peer.PeerId

    member this.RemoteEndPoint
        with get(): IPEndPoint = this.Client.Client.RemoteEndPoint :?> IPEndPoint

    member this.SendBytes (plaintext: array<byte>): Async<TransportStream> = async {
        let peer = this.Peer
        let ciphertext, channelEncryptor =
            PeerChannelEncryptor.encryptMessage plaintext peer.ChannelEncryptor
        let peer = { peer with ChannelEncryptor = channelEncryptor }
        let stream = this.Client.GetStream()
        do! stream.WriteAsync(ciphertext, 0, ciphertext.Length) |> Async.AwaitTask
        return { this with Peer = peer }
    }

    member this.RecvBytes(): Async<TransportStream * array<byte>> = async {
        let peer = this.Peer
        let stream = this.Client.GetStream()
        let! encryptedLength = TransportStream.ReadExactAsync stream TransportStream.bolt08EncryptedMessageLengthPrefixLength
        let res =
            PeerChannelEncryptor.decryptLengthHeader encryptedLength peer.ChannelEncryptor
        let length, channelEncryptor =
            match res with
            | Ok ok -> ok
            | Error transportError ->
                raise <| DecryptionException transportError
        let! ciphertext =
            try
                TransportStream.ReadExactAsync
                    stream
                    (int length + TransportStream.bolt08EncryptedMessageMacLength)
            with
            | :? PeerDisconnectedException -> raise <| PeerDisconnectedUngracefullyException()
        let res =
            PeerChannelEncryptor.decryptMessage ciphertext channelEncryptor
        let plaintext, channelEncryptor =
            match res with
            | Ok ok -> ok
            | Error transportError ->
                raise <| DecryptionException transportError
        let peer = { peer with ChannelEncryptor = channelEncryptor }
        let transportStream = { this with Peer = peer }
        return transportStream, plaintext
    }

