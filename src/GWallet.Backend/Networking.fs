namespace GWallet.Backend

open System
open System.Net.Sockets

type internal UnhandledSocketException =
    inherit Exception

    new(socketErrorCode: int, innerException: Exception) =
        { inherit Exception(sprintf "GWallet not prepared for this SocketException with ErrorCode[%d]" socketErrorCode,
                                    innerException) }

type ConnectionUnsuccessfulException =
    inherit Exception

    new(message: string, innerException: Exception) = { inherit Exception(message, innerException) }
    new(message: string) = { inherit Exception(message) }
    new() = { inherit Exception() }

type ServerRefusedException(message:string, innerException: Exception) =
   inherit ConnectionUnsuccessfulException (message, innerException)

type ServerTimedOutException =
   inherit ConnectionUnsuccessfulException

   new(message: string, innerException: Exception) = { inherit ConnectionUnsuccessfulException(message, innerException) }
   new() = { inherit ConnectionUnsuccessfulException() }

type ServerUnreachableException(message:string, innerException: Exception) =
   inherit ConnectionUnsuccessfulException (message, innerException)

module Networking =

    let FindSocketExceptionToRethrow (ex: Exception) (newExceptionMsg): Option<Exception> =
        let maybeSocketException = FSharpUtil.FindException<SocketException> ex
        match maybeSocketException with
        | None -> None
        | Some socketException ->
            if socketException.ErrorCode = int SocketError.ConnectionRefused then
                ServerRefusedException(newExceptionMsg, ex) :> Exception |> Some
            elif socketException.ErrorCode = int SocketError.ConnectionReset then
                ServerRefusedException(newExceptionMsg, ex) :> Exception |> Some

            elif socketException.ErrorCode = int SocketError.TimedOut then
                ServerTimedOutException(newExceptionMsg, ex) :> Exception |> Some

            // probably misleading errorCode (see fixed mono bug: https://github.com/mono/mono/pull/8041 )
            // TODO: remove this when Mono X.Y (where X.Y=version to introduce this bugfix) is stable
            //       everywhere (probably 8 years from now?), and see if we catch it again in sentry
            elif socketException.ErrorCode = int SocketError.AddressFamilyNotSupported then
                ServerUnreachableException(newExceptionMsg, ex) :> Exception |> Some

            elif socketException.ErrorCode = int SocketError.HostUnreachable then
                ServerUnreachableException(newExceptionMsg, ex) :> Exception |> Some
            elif socketException.ErrorCode = int SocketError.NetworkUnreachable then
                ServerUnreachableException(newExceptionMsg, ex) :> Exception |> Some
            elif socketException.ErrorCode = int SocketError.AddressNotAvailable then
                ServerUnreachableException(newExceptionMsg, ex) :> Exception |> Some
            elif socketException.ErrorCode = int SocketError.NetworkDown then
                ServerUnreachableException(newExceptionMsg, ex) :> Exception |> Some

            else
                UnhandledSocketException(socketException.ErrorCode, ex) :> Exception |> Some

