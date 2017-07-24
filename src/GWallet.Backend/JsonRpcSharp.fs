namespace GWallet.Backend

open System
open System.Linq
open System.Text
open System.Net.Sockets

module JsonRpcSharp =

    type Client (host: string, port: int) =
        let tcpClient = new TcpClient()

        let rec WrapResult (acc: byte list): string =
            let reverse = List.rev acc
            Encoding.UTF8.GetString(reverse.ToArray())

        let rec ReadInternal (stream: NetworkStream) acc dataAvailable: string =
            let canRead = stream.CanRead
            if (not (dataAvailable)) || (not (canRead)) then
                WrapResult acc
            else
                let byteInt = stream.ReadByte()
                if (byteInt = -1) then
                    WrapResult acc
                else
                    let byte = Convert.ToByte(byteInt)
                    ReadInternal stream (byte::acc) stream.DataAvailable

        let Read (stream: NetworkStream) =
            ReadInternal stream [] true

        member self.Request (request: string): string =
            if not (tcpClient.Connected) then
                tcpClient.Connect(host, port)
            let stream = tcpClient.GetStream()
            let bytes = Encoding.UTF8.GetBytes(request + "\n");
            stream.Write(bytes, 0, bytes.Length)
            stream.Flush()
            Read stream

        interface IDisposable with
            member x.Dispose() =
                tcpClient.Close()
