namespace GWallet.Backend

open System
open System.Linq
open System.Text
open System.Net
open System.Net.Sockets
open System.Threading

open GWallet.Backend.FSharpUtil

module JsonRpcSharpOld =

    exception ServerUnresponsiveException
    exception NoResponseReceivedAfterRequestException

    type LegacyTcpClient  (resolveHostAsync: unit->Async<IPAddress>, port: uint32) =
        let rec WrapResult (acc: List<byte>): string =
            let reverse = List.rev acc
            Encoding.UTF8.GetString(reverse.ToArray())

        let rec ReadByte (stream: NetworkStream): Maybe<byte> =
            let byteInt = stream.ReadByte()
            if (byteInt = -1) then
                Nothing
            else
                Convert.ToByte byteInt |> Just

        let DEFAULT_TIMEOUT_FOR_FIRST_DATA_AVAILABLE_SIGNAL_TO_HAPPEN = Config.DEFAULT_NETWORK_TIMEOUT
        let DEFAULT_TIMEOUT_FOR_SUBSEQUENT_DATA_AVAILABLE_SIGNAL_TO_HAPPEN = TimeSpan.FromMilliseconds(500.0)
        let DEFAULT_TIME_TO_WAIT_BETWEEN_DATA_GAPS = Config.DEFAULT_NETWORK_CONNECT_TIMEOUT
        let rec ReadInternal (stream: NetworkStream) acc (initTime: DateTime): string =
            let timeIsUp (): bool =
                if (List.Empty = acc) then
                    if (DateTime.UtcNow > initTime + DEFAULT_TIMEOUT_FOR_FIRST_DATA_AVAILABLE_SIGNAL_TO_HAPPEN) then
                        raise NoResponseReceivedAfterRequestException
                    else
                        false
                else
                    (DateTime.UtcNow > initTime + DEFAULT_TIMEOUT_FOR_SUBSEQUENT_DATA_AVAILABLE_SIGNAL_TO_HAPPEN)

            if (not (stream.DataAvailable)) || (not (stream.CanRead)) then
                if (timeIsUp()) then
                    WrapResult acc
                else
                    Thread.Sleep(DEFAULT_TIME_TO_WAIT_BETWEEN_DATA_GAPS)
                    ReadInternal stream acc initTime
            else
                match ReadByte stream with
                | Nothing -> WrapResult acc
                | Just byte ->
                    ReadInternal stream (byte::acc) DateTime.UtcNow

        let Read (stream: NetworkStream): string =
            ReadInternal stream List.Empty DateTime.UtcNow

        let Connect(): System.Net.Sockets.TcpClient =

            let host = resolveHostAsync() |> Async.RunSynchronously

            let tcpClient = new System.Net.Sockets.TcpClient(host.AddressFamily)
            tcpClient.SendTimeout <- Convert.ToInt32 Config.DEFAULT_NETWORK_TIMEOUT.TotalMilliseconds
            tcpClient.ReceiveTimeout <- Convert.ToInt32 Config.DEFAULT_NETWORK_TIMEOUT.TotalMilliseconds

            let connectTask = tcpClient.ConnectAsync(host, int port)

            if not (connectTask.Wait(Config.DEFAULT_NETWORK_TIMEOUT)) then
                raise ServerUnresponsiveException
            tcpClient

        new(host: IPAddress, port: uint32) = new LegacyTcpClient((fun () -> async { return host }), port)

        member self.Request (request: string): Async<string> = async {
            use tcpClient = Connect()
            let stream = tcpClient.GetStream()
            if not stream.CanTimeout then
                failwith "Inner NetworkStream should allow to set Read/Write Timeouts"
            stream.ReadTimeout <- Convert.ToInt32 Config.DEFAULT_NETWORK_TIMEOUT.TotalMilliseconds
            stream.WriteTimeout <- Convert.ToInt32 Config.DEFAULT_NETWORK_TIMEOUT.TotalMilliseconds
            let bytes = Encoding.UTF8.GetBytes(request + "\n");
            stream.Write(bytes, 0, bytes.Length)
            stream.Flush()
            return Read stream
        }