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

    type ConnectionUnsuccessfulException =
        inherit Exception

        new(message: string, innerException: Exception) = { inherit Exception(message, innerException) }
        new(message: string) = { inherit Exception(message) }
        new() = { inherit Exception() }

    type NoResponseReceivedAfterRequestException() =
       inherit ConnectionUnsuccessfulException()

    type ServerUnresponsiveException() =
       inherit ConnectionUnsuccessfulException()

    // Translation of https://github.com/davidfowl/TcpEcho/blob/master/src/Program.cs
    // BIG TODO:
    //   1. CONVERT THIS TO BE A CLASS THAT INHERITS FROM ClientBase CLASS
    //   2. STOP USING BLOCKING API TO USE ASYNC API INSTEAD (e.g. from Thread.Sleep to Task.Delay)
    type TcpClient (hostAndPort: unit->IPAddress*int) =

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

        let ReceiveAsync (socket: Socket) memory socketFlags =
            let arraySegment = GetArray memory
            SocketTaskExtensions.ReceiveAsync(socket, arraySegment, socketFlags)

        let GetAsciiString (buffer: ReadOnlySequence<byte>) =
            // A likely better way of converting this buffer/sequence to a string can be found her:
            // https://blogs.msdn.microsoft.com/dotnet/2018/07/09/system-io-pipelines-high-performance-io-in-net/
            // But I cannot find the namespace of the presumably extension method "Create()" on System.String:
            ref buffer
            |> System.Buffers.BuffersExtensions.ToArray
            |> System.Text.Encoding.ASCII.GetString

        let rec ReadPipeInternal (reader: PipeReader) (stringBuilder: StringBuilder) =
            let processLine (line:ReadOnlySequence<byte>) =
                line |> GetAsciiString |> stringBuilder.AppendLine |> ignore

            // TODO: convert to async!
            let result = reader.ReadAsync().Result

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
                ReadPipeInternal reader stringBuilder
            else
                stringBuilder.ToString()

        let ReadPipe pipeReader =
            ReadPipeInternal pipeReader (StringBuilder())

        let FillPipeAsync (socket: Socket) (writer: PipeWriter) =
            // If incomplete messages become an issue here, consider reinstating the while/loop
            // logic from the original C# source.  For now, assuming that every response is complete
            // is working better than trying to handle potential incomplete responses.
            let memory: Memory<byte> = writer.GetMemory minimumBufferSize
            let receiveTask = ReceiveAsync socket memory SocketFlags.None
            if receiveTask.Wait Config.DEFAULT_NETWORK_TIMEOUT then
                let bytesRead = receiveTask.Result
                if bytesRead > 0 then
                    writer.Advance bytesRead
                else
                    raise <| NoResponseReceivedAfterRequestException()
            else
                raise <| NoResponseReceivedAfterRequestException()
            writer.Complete()

        let Connect () =
            let host, port = hostAndPort()
            let socket = new Socket(SocketType.Stream,
                                    ProtocolType.Tcp)
                                    // Not using timeout properties on Socket because FillPipeAsync retrieves data
                                    // in a Task which we have timeout itself. But keep in mind that these socket
                                    // timeout properties exist, and may prove to have some use:
                                    //, SendTimeout = defaultNetworkTimeout, ReceiveTimeout = defaultNetworkTimeout)

            socket.Connect(host, port)
            socket

        new(host: IPAddress, port: int) = new TcpClient(fun _ -> host, port)

        member __.Request (request: string): string =
            use socket = Connect ()
            async {
                let buffer =
                    request + "\n"
                    |> Encoding.UTF8.GetBytes
                    |> ArraySegment<byte>

                return! socket.SendAsync(buffer, SocketFlags.None) |> Async.AwaitTask
            }
                // TODO: convert to async!
                |> Async.RunSynchronously

                |> ignore // int bytesSent

            let pipe = Pipe()
            FillPipeAsync socket pipe.Writer
            ReadPipe pipe.Reader

    type LegacyTcpClient  (hostAndPort: unit->IPAddress*int) =
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

        let Connect(): System.Net.Sockets.TcpClient =

            let host,port = hostAndPort()

            let tcpClient = new System.Net.Sockets.TcpClient(host.AddressFamily)
            tcpClient.SendTimeout <- Convert.ToInt32 Config.DEFAULT_NETWORK_TIMEOUT.TotalMilliseconds
            tcpClient.ReceiveTimeout <- Convert.ToInt32 Config.DEFAULT_NETWORK_TIMEOUT.TotalMilliseconds

            let connectTask = tcpClient.ConnectAsync(host, port)

            if not (connectTask.Wait(Config.DEFAULT_NETWORK_TIMEOUT)) then
                raise(ServerUnresponsiveException())
            tcpClient

        new(host: IPAddress, port: int) = new LegacyTcpClient(fun _ -> host, port)

        member self.Request (request: string): string =
            use tcpClient = Connect()
            let stream = tcpClient.GetStream()
            if not stream.CanTimeout then
                failwith "Inner NetworkStream should allow to set Read/Write Timeouts"
            stream.ReadTimeout <- Convert.ToInt32 Config.DEFAULT_NETWORK_TIMEOUT.TotalMilliseconds
            stream.WriteTimeout <- Convert.ToInt32 Config.DEFAULT_NETWORK_TIMEOUT.TotalMilliseconds
            let bytes = Encoding.UTF8.GetBytes(request + "\n");
            stream.Write(bytes, 0, bytes.Length)
            stream.Flush()
            Read stream
