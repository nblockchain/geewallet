namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Net.Sockets
open System.Net
open System.Diagnostics

open NBitcoin

open DotNetLightning.Utils
open DotNetLightning.Peer

open GWallet.Backend
open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks
open GWallet.Backend.UtxoCoin.Lightning.Util

open FSharp.Core

type PeerDisconnectedError = {
    Abruptly: bool
} with
    member this.Message =
        if this.Abruptly then
            "peer disconnected after sending a partial message"
        else
            "peer disconnected"
    member this.PossibleBug =
        not this.Abruptly

type HandshakeError =
    | TcpConnect of seq<SocketException>
    | DisconnectedOnAct1 of PeerDisconnectedError
    | InvalidAct1 of PeerError
    | DisconnectedOnAct2 of PeerDisconnectedError
    | InvalidAct2 of PeerError
    | DisconnectedOnAct3 of PeerDisconnectedError
    | InvalidAct3 of PeerError
    with
    member this.Message =
        match this with
        | TcpConnect errs ->
            let messages = Seq.map (fun (err: SocketException) -> err.Message) errs
            SPrintF1 "TCP connection failed: %s" (String.concat "; " messages)
        | DisconnectedOnAct1 err ->
            SPrintF1 "Peer disconnected before starting handshake: %s" err.Message
        | InvalidAct1 err ->
            SPrintF1 "Invalid handshake act 1: %s" err.Message
        | DisconnectedOnAct2 err ->
            SPrintF1 "Peer disconnected before sending handshake act 2: %s" err.Message
        | InvalidAct2 err ->
            SPrintF1 "Invalid handshake act 2: %s" err.Message
        | DisconnectedOnAct3 err ->
            SPrintF1 "Peer disconnected before sending handshake act 3: %s" err.Message
        | InvalidAct3 err ->
            SPrintF1 "Invalid handshake act 3: %s" err.Message
    member this.PossibleBug =
        match this with
        | DisconnectedOnAct1 _
        | DisconnectedOnAct2 _
        | DisconnectedOnAct3 _ -> true
        | TcpConnect _
        | InvalidAct1 _
        | InvalidAct2 _
        | InvalidAct3 _ -> false

type RecvBytesError =
    | PeerDisconnected of PeerDisconnectedError
    | Decryption of PeerError
    with
    member this.Message =
        match this with
        | PeerDisconnected err ->
            SPrintF1 "Peer disconnected: %s" err.Message
        | Decryption err ->
            SPrintF1 "Error decrypting message from peer: %s" err.Message
    member this.PossibleBug =
        match this with
        | PeerDisconnected err -> err.PossibleBug
        | Decryption _ -> false

type TransportListener = {
    NodeSecret: ExtKey
    Listener: TcpListener
} with
    interface IDisposable with
        member this.Dispose() =
            this.Listener.Stop()

    static member Bind (nodeSecret: ExtKey) (endpoint: IPEndPoint) =
        let listener = new TcpListener (endpoint)
        listener.Start()
        {
            NodeSecret = nodeSecret
            Listener = listener
        }

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
                                             : Async<Result<array<byte>, PeerDisconnectedError>> =
        let buf: array<byte> = Array.zeroCreate numberBytesToRead
        let rec read buf totalBytesRead = async {
            let! bytesRead =
                stream.ReadAsync(buf, totalBytesRead, (numberBytesToRead - totalBytesRead))
                |> Async.AwaitTask
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
        }
        read buf 0

    static member Connect (nodeSecret: ExtKey)
                          (peerNodeId: NodeId)
                          (peerId: PeerId)
                              : Async<Result<TransportStream, HandshakeError>> = async {
        let nodeSecretKey = nodeSecret.PrivateKey
        let peerEndpoint = peerId.Value :?> IPEndPoint
        let client = new TcpClient (peerId.Value.AddressFamily)
        Console.WriteLine(SPrintF1 "Connecting over TCP to %A..." peerEndpoint)
        let! connectRes = async {
            try
                do! client.ConnectAsync(peerEndpoint.Address, peerEndpoint.Port) |> Async.AwaitTask
                return Ok()
            with
            | ex ->
                let socketExceptions = FindSingleException<SocketException> ex
                return Error <| TcpConnect socketExceptions
        }
        match connectRes with
        | Error err -> return Error err
        | Ok () ->
            let stream = client.GetStream()

            let peer = Peer.CreateOutbound(peerId, peerNodeId, nodeSecretKey)
            let act1, peerEncryptor = PeerChannelEncryptor.getActOne peer.ChannelEncryptor
            Debug.Assert((TransportStream.bolt08ActOneLength = act1.Length), "act1 has wrong length")
            let peerAfterAct1 = { peer with ChannelEncryptor = peerEncryptor }

            // Send act1
            do! stream.WriteAsync(act1, 0, act1.Length) |> Async.AwaitTask

            // Receive act2
            Infrastructure.LogDebug "Receiving Act 2..."
            let! act2Res = TransportStream.ReadExactAsync stream TransportStream.bolt08ActTwoLength
            match act2Res with
            | Error peerDisconnectedError -> return Error <| DisconnectedOnAct2 peerDisconnectedError
            | Ok act2 ->
                let peerCmd = ProcessActTwo(act2, nodeSecretKey)
                match Peer.executeCommand peerAfterAct1 peerCmd with
                | Error err -> return Error <| InvalidAct2 err
                | Ok (ActTwoProcessed ((act3, _), _) as evt::[]) ->
                    let peerAfterAct2 = Peer.applyEvent peerAfterAct1 evt

                    Debug.Assert((TransportStream.bolt08ActThreeLength = act3.Length), SPrintF1 "act3 has wrong length (not %i)" TransportStream.bolt08ActThreeLength)

                    do! stream.WriteAsync(act3, 0, act3.Length) |> Async.AwaitTask
                    return Ok {
                        NodeSecret = nodeSecret
                        Peer = peerAfterAct2
                        Client = client
                    }
                | Ok evts ->
                    return failwith <| SPrintF1
                        "DNL returned unexpected events when processing act2: %A" evts
    }

    static member AcceptFromTransportListener (transportListener: TransportListener)
                                                  : Async<Result<TransportStream, HandshakeError>> = async {
        let! client = transportListener.Listener.AcceptTcpClientAsync() |> Async.AwaitTask
        let nodeSecretKey = transportListener.NodeSecret.PrivateKey
        let stream = client.GetStream()
        let peerId = client.Client.RemoteEndPoint |> PeerId
        let peer = Peer.CreateInbound(peerId, nodeSecretKey)
        let! act1Res = TransportStream.ReadExactAsync stream TransportStream.bolt08ActOneLength
        match act1Res with
        | Error peerDisconnectedError -> return Error <| DisconnectedOnAct1 peerDisconnectedError
        | Ok act1 ->
            let peerCmd = ProcessActOne(act1, nodeSecretKey)
            match Peer.executeCommand peer peerCmd with
            | Error err -> return Error <| InvalidAct1 err
            | Ok (ActOneProcessed(act2, _) as evt::[]) ->
                let peerAfterAct2 = Peer.applyEvent peer evt
                do! stream.WriteAsync(act2, 0, act2.Length) |> Async.AwaitTask
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
                            NodeSecret = transportListener.NodeSecret
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

    member this.RemoteNodeId
        with get(): NodeId =
            UnwrapOption
                this.Peer.TheirNodeId
                "The TransportStream type is created by performing a handshake \
                and in doing so guarantees that the peer's node id is known"

    member this.PeerId
        with get(): PeerId = this.Peer.PeerId

    member this.RemoteEndPoint
        with get(): IPEndPoint = this.Client.Client.RemoteEndPoint :?> IPEndPoint

    member this.SendBytes (plaintext: array<byte>): Async<TransportStream> = async {
        let peer = this.Peer
        let ciphertext, channelEncryptor =
            PeerChannelEncryptor.encryptMessage plaintext peer.ChannelEncryptor
        let peerAfterBytesSent = { peer with ChannelEncryptor = channelEncryptor }
        let stream = this.Client.GetStream()
        do! stream.WriteAsync(ciphertext, 0, ciphertext.Length) |> Async.AwaitTask
        return { this with Peer = peerAfterBytesSent }
    }

    member this.RecvBytes(): Async<Result<TransportStream * array<byte>, RecvBytesError>> = async {
        let peer = this.Peer
        let stream = this.Client.GetStream()
        let! encryptedLengthRes = TransportStream.ReadExactAsync stream TransportStream.bolt08EncryptedMessageLengthPrefixLength
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
                        let transportStream = { this with Peer = peerAfterBytesReceived }
                        return Ok (transportStream, plaintext)
    }

