namespace GWallet.Backend

open System
open System.Linq
open System.Text
open System.Net.Sockets
open System.Threading

module JsonRpcSharp =

    type ConnectionUnsuccessfulException =
        inherit Exception

        new(message: string, innerException: Exception) = { inherit Exception(message, innerException) }
        new() = { inherit Exception() }

    type NoResponseReceivedAfterRequestException() =
       inherit ConnectionUnsuccessfulException()

    type TlsNotSupportedYetInGWalletException() =
       inherit ConnectionUnsuccessfulException()

    type ServerRefusedException(message:string, innerException: Exception) =
       inherit ConnectionUnsuccessfulException (message, innerException)

    type ServerTimedOutException(message:string, innerException: Exception) =
       inherit ConnectionUnsuccessfulException (message, innerException)

    type ServerCannotBeResolvedException(message:string, innerException: Exception) =
       inherit ConnectionUnsuccessfulException (message, innerException)

    type ServerUnresponsiveException() =
       inherit ConnectionUnsuccessfulException()

    type ServerUnreachableException(message:string, innerException: Exception) =
       inherit ConnectionUnsuccessfulException (message, innerException)

    type Client (host: string, port: int) =
        let tcpClient = new TcpClient()

        let rec WrapResult (acc: byte list): string =
            let reverse = List.rev acc
            Encoding.UTF8.GetString(reverse.ToArray())

        let rec ReadByte (stream: NetworkStream): Option<byte> =
            let byteInt = stream.ReadByte()
            if (byteInt = -1) then
                None
            else
                Some(Convert.ToByte(byteInt))

        let DEFAULT_TIMEOUT_FOR_FIRST_DATA_AVAILABLE_SIGNAL_TO_HAPPEN = Config.DEFAULT_NETWORK_TIMEOUT
        let DEFAULT_TIMEOUT_FOR_SUBSEQUENT_DATA_AVAILABLE_SIGNAL_TO_HAPPEN = TimeSpan.FromMilliseconds(500.0)
        let DEFAULT_TIME_TO_WAIT_BETWEEN_DATA_GAPS = TimeSpan.FromMilliseconds(1.0)
        let rec ReadInternal (stream: NetworkStream) acc (initTime: DateTime): string =
            let timeIsUp (): bool =
                if (acc = []) then
                    if (DateTime.Now > initTime + DEFAULT_TIMEOUT_FOR_FIRST_DATA_AVAILABLE_SIGNAL_TO_HAPPEN) then
                        raise(NoResponseReceivedAfterRequestException())
                    else
                        false
                else
                    (DateTime.Now > initTime + DEFAULT_TIMEOUT_FOR_SUBSEQUENT_DATA_AVAILABLE_SIGNAL_TO_HAPPEN)

            let canRead = stream.CanRead
            if (not (stream.DataAvailable)) || (not (stream.CanRead)) then
                if (timeIsUp()) then
                    WrapResult acc
                else
                    Thread.Sleep(DEFAULT_TIME_TO_WAIT_BETWEEN_DATA_GAPS)
                    ReadInternal stream acc initTime
            else
                match ReadByte stream with
                | None -> WrapResult acc
                | Some(byte) ->
                    ReadInternal stream (byte::acc) DateTime.Now

        let Read (stream: NetworkStream): string =
            ReadInternal stream [] DateTime.Now

        let Connect(): unit =
            tcpClient.SendTimeout <- Convert.ToInt32 Config.DEFAULT_NETWORK_TIMEOUT.TotalMilliseconds
            tcpClient.ReceiveTimeout <- Convert.ToInt32 Config.DEFAULT_NETWORK_TIMEOUT.TotalMilliseconds

            try
                let connectTask = tcpClient.ConnectAsync(host,port)

                if not (connectTask.Wait(Config.DEFAULT_NETWORK_TIMEOUT)) then
                    raise(ServerUnresponsiveException())
            with
            | ex ->
                let msg = "JsonRpcSharp faced some problem when trying communication"
                let socketException = FSharpUtil.FindException<SocketException>(ex)
                if (socketException.IsNone) then
                    reraise()
                if (socketException.Value.ErrorCode = int SocketError.ConnectionRefused) then
                    raise(ServerRefusedException(msg, ex))
                if (socketException.Value.ErrorCode = int SocketError.TimedOut) then
                    raise(ServerTimedOutException(msg, ex))
                if (socketException.Value.ErrorCode = int SocketError.HostNotFound) then
                    raise(ServerCannotBeResolvedException(msg, ex))
                if (socketException.Value.ErrorCode = int SocketError.NetworkUnreachable) then
                    raise(ServerUnreachableException(msg, ex))
                reraise()

        member self.Request (request: string): string =
            if not (tcpClient.Connected) then
                Connect()
            let stream = tcpClient.GetStream()
            if not stream.CanTimeout then
                failwith "Inner NetworkStream should allow to set Read/Write Timeouts"
            stream.ReadTimeout <- Convert.ToInt32 Config.DEFAULT_NETWORK_TIMEOUT.TotalMilliseconds
            stream.WriteTimeout <- Convert.ToInt32 Config.DEFAULT_NETWORK_TIMEOUT.TotalMilliseconds
            let bytes = Encoding.UTF8.GetBytes(request + "\n");
            stream.Write(bytes, 0, bytes.Length)
            stream.Flush()
            Read stream

        interface IDisposable with
            member x.Dispose() =
                tcpClient.Close()
