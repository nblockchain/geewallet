namespace GWallet.Backend

open System
open System.Net
open System.Net.Sockets

type ProtocolGlitchException(message: string, innerException: Exception) =
   inherit ConnectionUnsuccessfulException (message, innerException)

type ServerCannotBeResolvedException =
   inherit ConnectionUnsuccessfulException

   new(message) = { inherit ConnectionUnsuccessfulException(message) }
   new(message:string, innerException: Exception) = { inherit ConnectionUnsuccessfulException(message, innerException) }

type JsonRpcTcpClient (host: string, port: int) =

    let ResolveAsync (hostName: string): Async<Option<IPAddress>> = async {
        // FIXME: loop over all addresses?
        let! hostEntry = Dns.GetHostEntryAsync hostName |> Async.AwaitTask
        return hostEntry.AddressList |> Array.tryHead
    }

    let exceptionMsg = "JsonRpcSharp faced some problem when trying communication"

    let ResolveHost(): Async<IPAddress> = async {
        try
            let! maybeTimedOutipAddress = ResolveAsync host |> FSharpUtil.WithTimeout Config.DEFAULT_NETWORK_TIMEOUT
            match maybeTimedOutipAddress with
            | Some ipAddressOption ->
                match ipAddressOption with
                | Some ipAddress -> return ipAddress
                | None   -> return raise <| ServerCannotBeResolvedException
                                                (sprintf "DNS host entry lookup resulted in no records for %s" host)
            | None -> return raise <| TimeoutException (sprintf "Timed out connecting to %s:%i" host port)
        with
        | :? TimeoutException ->
            return raise(ServerCannotBeResolvedException(exceptionMsg))
        | ex ->
            let socketException = FSharpUtil.FindException<SocketException>(ex)
            if (socketException.IsNone) then
                return raise <| FSharpUtil.ReRaise ex
            if (socketException.Value.ErrorCode = int SocketError.HostNotFound ||
                socketException.Value.ErrorCode = int SocketError.NoData ||
                socketException.Value.ErrorCode = int SocketError.TryAgain) then
                return raise <| ServerCannotBeResolvedException(exceptionMsg, ex)
            return raise <| UnhandledSocketException(socketException.Value.ErrorCode, ex)
    }

    let rpcTcpClientInnerRequest =
        if Config.NewUtxoTcpClientDisabled then
            let tcpClient = JsonRpcSharpOld.LegacyTcpClient(ResolveHost, port)
            tcpClient.Request
        else
            let tcpClient = JsonRpcSharp.TcpClient.TcpClient(ResolveHost, port)
            fun jsonRequest -> tcpClient.Request jsonRequest None

    member self.Request (request: string): Async<string> = async {
        try
            let! stringOption = rpcTcpClientInnerRequest request |> FSharpUtil.WithTimeout Config.DEFAULT_NETWORK_TIMEOUT
            let str =
                match stringOption with
                | Some s -> s
                | None   -> raise <| ServerTimedOutException()
            return str
        with
        | :? ConnectionUnsuccessfulException as ex ->
            return raise <| FSharpUtil.ReRaise ex
        | :? JsonRpcSharpOld.ServerUnresponsiveException as ex ->
            return raise <| ServerTimedOutException(exceptionMsg, ex)
        | :? JsonRpcSharp.TcpClient.NoResponseReceivedAfterRequestException as ex ->
            return raise <| ServerTimedOutException(exceptionMsg, ex)

        // FIXME: we should log this one on Sentry as a warning because it's really strange, I bet it's a bug
        // on Mono that could maybe go away with higher versions of it (higher versions of Xamarin-Android), see
        // git blame to look at the whole stacktrace (ex.ToString())
        | :? NotSupportedException as nse ->
            return raise <| ProtocolGlitchException(exceptionMsg, nse)
        | ex ->
            let maybeWrappedSocketException = Networking.FindSocketExceptionToRethrow ex exceptionMsg
            match maybeWrappedSocketException with
            | None ->
                return raise <| FSharpUtil.ReRaise ex
            | Some rewrappedSocketException ->
                return raise rewrappedSocketException
    }
