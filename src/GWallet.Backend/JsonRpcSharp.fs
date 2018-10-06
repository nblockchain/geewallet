namespace GWallet.Backend

open System
open System.Buffers
open System.Linq
open System.Text
open System.IO.Pipelines
open System.Net
open System.Net.Sockets
open System.Runtime.InteropServices
open System.Threading

module JsonRpcSharp =

    exception NoResponseReceivedAfterRequestException
    exception ServerUnresponsiveException

    // Translation of https://github.com/davidfowl/TcpEcho/blob/master/src/Program.cs
    type TcpClient (resolveHostAsync: unit->Async<IPAddress>, port) =

        [<Literal>]
        let minimumBufferSize = 512

        let IfNotNull f x = x |> Option.ofNullable |> Option.iter f

        let GetArrayFromReadOnlyMemory memory: ArraySegment<byte> =
            match MemoryMarshal.TryGetArray memory with
            | true, segment -> segment
            | false, _      -> raise <| InvalidOperationException("Buffer backed by array was expected")

        let GetArray (memory: Memory<byte>) =
            Memory<byte>.op_Implicit memory
            |> GetArrayFromReadOnlyMemory

        let ReceiveAsync (socket: Socket) memory socketFlags = async {
            let arraySegment = GetArray memory
            return! SocketTaskExtensions.ReceiveAsync(socket, arraySegment, socketFlags) |> Async.AwaitTask
        }

        let GetAsciiString (buffer: ReadOnlySequence<byte>) =
            // A likely better way of converting this buffer/sequence to a string can be found her:
            // https://blogs.msdn.microsoft.com/dotnet/2018/07/09/system-io-pipelines-high-performance-io-in-net/
            // But I cannot find the namespace of the presumably extension method "Create()" on System.String:
            ref buffer
            |> System.Buffers.BuffersExtensions.ToArray
            |> System.Text.Encoding.ASCII.GetString

        let rec ReadPipeInternal (reader: PipeReader) (stringBuilder: StringBuilder) = async {
            let processLine (line:ReadOnlySequence<byte>) =
                line |> GetAsciiString |> stringBuilder.AppendLine |> ignore

            let! result = reader.ReadAsync().AsTask() |> Async.AwaitTask

            let rec keepAdvancingPosition buffer =
                // How to call a ref extension method using extension syntax?
                System.Buffers.BuffersExtensions.PositionOf(ref buffer, byte '\n')
                |> IfNotNull(fun pos ->
                    buffer.Slice(0, pos)
                    |> processLine
                    buffer.GetPosition(1L, pos)
                    |> buffer.Slice
                    |> keepAdvancingPosition)
            keepAdvancingPosition result.Buffer
            reader.AdvanceTo(result.Buffer.Start, result.Buffer.End)
            if not result.IsCompleted then
                return! ReadPipeInternal reader stringBuilder
            else
                return stringBuilder.ToString()
        }

        let ReadPipe pipeReader =
            ReadPipeInternal pipeReader (StringBuilder())

        let FillPipeAsync (socket: Socket) (writer: PipeWriter) = async {
            // If incomplete messages become an issue here, consider reinstating the while/loop
            // logic from the original C# source.  For now, assuming that every response is complete
            // is working better than trying to handle potential incomplete responses.
            let memory: Memory<byte> = writer.GetMemory minimumBufferSize
            let! bytesReceived = ReceiveAsync socket memory SocketFlags.None
            if bytesReceived > 0 then
                writer.Advance bytesReceived
            else
                raise NoResponseReceivedAfterRequestException
            writer.Complete()
        }

        let Connect () = async {
            let! host = resolveHostAsync()
            let socket = new Socket(SocketType.Stream,
                                    ProtocolType.Tcp)
                                    // Not using timeout properties on Socket because FillPipeAsync retrieves data
                                    // in a Task which we have timeout itself. But keep in mind that these socket
                                    // timeout properties exist, and may prove to have some use:
                                    //, SendTimeout = defaultNetworkTimeout, ReceiveTimeout = defaultNetworkTimeout)

            do! socket.ConnectAsync(host, port) |> Async.AwaitTask
            return socket
        }

        new(host: IPAddress, port: int) = new TcpClient((fun _ -> async { return host }), port)

        member __.Request (request: string): Async<string> = async {
            use! socket = Connect()
            let buffer =
                request + "\n"
                |> Encoding.UTF8.GetBytes
                |> ArraySegment<byte>

            let! bytesReceived = socket.SendAsync(buffer, SocketFlags.None) |> Async.AwaitTask
            let pipe = Pipe()
            do! FillPipeAsync socket pipe.Writer
            let! str = ReadPipe pipe.Reader
            return str
        }

    type LegacyTcpClient  (resolveHostAsync: unit->Async<IPAddress>, port) =
        let rec WrapResult (acc: List<byte>): string =
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
                if (List.Empty = acc) then
                    if (DateTime.Now > initTime + DEFAULT_TIMEOUT_FOR_FIRST_DATA_AVAILABLE_SIGNAL_TO_HAPPEN) then
                        raise NoResponseReceivedAfterRequestException
                    else
                        false
                else
                    (DateTime.Now > initTime + DEFAULT_TIMEOUT_FOR_SUBSEQUENT_DATA_AVAILABLE_SIGNAL_TO_HAPPEN)

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
            ReadInternal stream List.Empty DateTime.Now

        let Connect(): System.Net.Sockets.TcpClient =

            let host = resolveHostAsync() |> Async.RunSynchronously

            let tcpClient = new System.Net.Sockets.TcpClient(host.AddressFamily)
            tcpClient.SendTimeout <- Convert.ToInt32 Config.DEFAULT_NETWORK_TIMEOUT.TotalMilliseconds
            tcpClient.ReceiveTimeout <- Convert.ToInt32 Config.DEFAULT_NETWORK_TIMEOUT.TotalMilliseconds

            let connectTask = tcpClient.ConnectAsync(host, port)

            if not (connectTask.Wait(Config.DEFAULT_NETWORK_TIMEOUT)) then
                raise ServerUnresponsiveException
            tcpClient

        new(host: IPAddress, port: int) = new LegacyTcpClient((fun () -> async { return host }), port)

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