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
        let minimumBufferSize = 2048

        let GetArrayFromReadOnlyMemory memory: ArraySegment<byte> =
            match MemoryMarshal.TryGetArray memory with
            | true, segment -> segment
            | false, _      -> raise <| InvalidOperationException("Buffer backed by array was expected")

        let GetArray (memory: Memory<byte>) =
            Memory<byte>.op_Implicit memory
            |> GetArrayFromReadOnlyMemory

        let ReceiveAsync (socket: Socket) memory socketFlags = async {
            let arraySegment = GetArray memory
            return! socket.ReceiveAsync(arraySegment, socketFlags) |> Async.AwaitTask
        }

        let GetAsciiString (buffer: ReadOnlySequence<byte>) =
            // FIXME: in newer versions of F#, this mutable wrapper is not needed (remove when we depend on it)
            let mutable mutableBuffer = buffer

            // A likely better way of converting this buffer/sequence to a string can be found her:
            // https://blogs.msdn.microsoft.com/dotnet/2018/07/09/system-io-pipelines-high-performance-io-in-net/
            // But I cannot find the namespace of the presumably extension method "Create()" on System.String:
            let bufferArray = System.Buffers.BuffersExtensions.ToArray (& mutableBuffer)
            System.Text.Encoding.ASCII.GetString bufferArray

        let rec ReadPipeInternal (reader: PipeReader) (stringBuilder: StringBuilder) = async {
            let processLine (line:ReadOnlySequence<byte>) =
                line |> GetAsciiString |> stringBuilder.AppendLine |> ignore

            let rec keepAdvancingPosition (buffer: ReadOnlySequence<byte>): ReadOnlySequence<byte> =
                // FIXME: in newer versions of F#, this mutable wrapper is not needed (remove when we depend on it)
                let mutable mutableBuffer = buffer

                // How to call a ref extension method using extension syntax?
                let maybePosition = System.Buffers.BuffersExtensions.PositionOf(& mutableBuffer, byte '\n')
                                    |> Option.ofNullable
                match maybePosition with
                | None ->
                    buffer
                | Some pos ->
                    buffer.Slice(0, pos)
                    |> processLine
                    let nextBuffer = buffer.GetPosition(1L, pos)
                                     |> buffer.Slice
                    keepAdvancingPosition nextBuffer

            let! result = (reader.ReadAsync().AsTask() |> Async.AwaitTask)

            let lastBuffer = keepAdvancingPosition result.Buffer
            reader.AdvanceTo(lastBuffer.Start, lastBuffer.End)
            if not result.IsCompleted then
                return! ReadPipeInternal reader stringBuilder
            else
                reader.Complete()
                return stringBuilder.ToString()
        }

        let ReadFromPipe pipeReader = async {
            let! result = Async.Catch (ReadPipeInternal pipeReader (StringBuilder()))
            return result
        }

        let WriteIntoPipeAsync (socket: Socket) (writer: PipeWriter) = async {
            let rec WritePipeInternal() = async {
                let! bytesReceived = ReceiveAsync socket (writer.GetMemory minimumBufferSize) SocketFlags.None 
                if bytesReceived > 0 then
                    writer.Advance bytesReceived
                    let! result = (writer.FlushAsync().AsTask() |> Async.AwaitTask)
                    let dataAvailableInSocket = socket.Available
                    if  dataAvailableInSocket > 0 && not result.IsCompleted then
                        return! WritePipeInternal()
                    else
                        return String.Empty
                else
                    return raise NoResponseReceivedAfterRequestException
            }
            let! result = Async.Catch (WritePipeInternal())
            writer.Complete()
            return result
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

            let! bytesSent = socket.SendAsync(buffer, SocketFlags.None) |> Async.AwaitTask
            let pipe = Pipe()

            let writerJob = WriteIntoPipeAsync socket pipe.Writer
            let readerJob = ReadFromPipe pipe.Reader
            let bothJobs = Async.Parallel [writerJob;readerJob]

            let! writerAndReaderResults = bothJobs
            let writerResult =  writerAndReaderResults
                                |> Seq.head
            let readerResult =  writerAndReaderResults
                                |> Seq.last

            return match writerResult with
                   | Choice1Of2 _ ->
                       match readerResult with
                       // reading result
                       | Choice1Of2 str -> str
                       // possible reader pipe exception
                       | Choice2Of2 ex -> raise ex
                   // possible socket reading exception
                   | Choice2Of2 ex -> raise ex
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