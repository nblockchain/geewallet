namespace GWallet.Backend

open System
open System.Net
open System.Net.Sockets
open System.Threading.Tasks
open System.Runtime.ExceptionServices

type internal UnhandledSocketException =
    inherit Exception

    new(socketErrorCode: int, innerException: Exception) =
        { inherit Exception(sprintf "GWallet not prepared for this SocketException with ErrorCode[%d]" socketErrorCode,
                                    innerException) }

type ServerRefusedException(message:string, innerException: Exception) =
   inherit JsonRpcSharp.ConnectionUnsuccessfulException (message, innerException)

type ServerTimedOutException(message:string, innerException: Exception) =
   inherit JsonRpcSharp.ConnectionUnsuccessfulException (message, innerException)

type ProtocolGlitchException(message: string, innerException: Exception) =
   inherit JsonRpcSharp.ConnectionUnsuccessfulException (message, innerException)

type ServerCannotBeResolvedException =
   inherit JsonRpcSharp.ConnectionUnsuccessfulException

   new(message) = { inherit JsonRpcSharp.ConnectionUnsuccessfulException(message) }
   new(message:string, innerException: Exception) = { inherit JsonRpcSharp.ConnectionUnsuccessfulException(message, innerException) }

type ServerUnreachableException(message:string, innerException: Exception) =
   inherit JsonRpcSharp.ConnectionUnsuccessfulException (message, innerException)

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
        let monoVersion = Config.GetMonoVersion()
        //we need this check because Ubuntu 18.04 LTS still brings a very old version of Mono (4.6.2) that has a runtime bug
        if monoVersion.IsSome || monoVersion.Value < Version("5.4") then
            let tcpClient = JsonRpcSharp.LegacyTcpClient(ResolveHost, port)
            tcpClient.Request
        else
            let tcpClient = JsonRpcSharp.TcpClient(ResolveHost, port)
            tcpClient.Request

    member self.Request (request: string): Async<string> = async {
        try
            let! stringOption = rpcTcpClientInnerRequest request |> FSharpUtil.WithTimeout Config.DEFAULT_NETWORK_TIMEOUT
            let str =
                match stringOption with
                | Some s -> s
                | None   -> raise <| JsonRpcSharp.NoResponseReceivedAfterRequestException()
            return str
        with
        | :? JsonRpcSharp.ConnectionUnsuccessfulException as ex ->
            return raise <| FSharpUtil.ReRaise ex

        // FIXME: we should log this one on Sentry as a warning because it's really strange, I bet it's a bug
        // on Mono that could maybe go away with higher versions of it (higher versions of Xamarin-Android), see
        // git blame to look at the whole stacktrace (ex.ToString())
        | :? NotSupportedException as nse ->
            return raise <| ProtocolGlitchException(exceptionMsg, nse)
        | ex ->
            let socketException = FSharpUtil.FindException<SocketException>(ex)
            if (socketException.IsNone) then
                ExceptionDispatchInfo.Capture(ex).Throw()
            if (socketException.Value.ErrorCode = int SocketError.ConnectionRefused) then
                return raise <| ServerRefusedException(exceptionMsg, ex)
            if socketException.Value.ErrorCode = int SocketError.ConnectionReset then
                return raise <| ServerRefusedException(exceptionMsg, ex)

            if (socketException.Value.ErrorCode = int SocketError.TimedOut) then
                return raise <| ServerTimedOutException(exceptionMsg, ex)

            // probably misleading errorCode (see fixed mono bug: https://github.com/mono/mono/pull/8041 )
            // TODO: remove this when Mono X.Y (where X.Y=version to introduce this bugfix) is stable
            //       everywhere (probably 8 years from now?), and see if we catch it again in sentry
            if (socketException.Value.ErrorCode = int SocketError.AddressFamilyNotSupported) then
                return raise <| ServerUnreachableException(exceptionMsg, ex)

            if (socketException.Value.ErrorCode = int SocketError.HostUnreachable) then
                return raise <| ServerUnreachableException(exceptionMsg, ex)
            if (socketException.Value.ErrorCode = int SocketError.NetworkUnreachable) then
                return raise <| ServerUnreachableException(exceptionMsg, ex)
            if (socketException.Value.ErrorCode = int SocketError.AddressNotAvailable) then
                return raise <| ServerUnreachableException(exceptionMsg, ex)
            if socketException.Value.ErrorCode = int SocketError.NetworkDown then
                return raise <| ServerUnreachableException(exceptionMsg, ex)

            return raise(UnhandledSocketException(socketException.Value.ErrorCode, ex))
    }
