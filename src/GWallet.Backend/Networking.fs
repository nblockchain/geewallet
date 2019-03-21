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

type BuggyExceptionFromOldMonoVersion (message: string, innerException: Exception) =
    inherit ConnectionUnsuccessfulException (message, innerException)

type ServerClosedConnectionEarlyException(message: string, innerException: Exception) =
    inherit ConnectionUnsuccessfulException (message, innerException)

type ServerRefusedException(message:string, innerException: Exception) =
   inherit ConnectionUnsuccessfulException (message, innerException)

type ServerTimedOutException =
   inherit ConnectionUnsuccessfulException

   new(message: string, innerException: Exception) = { inherit ConnectionUnsuccessfulException(message, innerException) }
   new(message) = { inherit ConnectionUnsuccessfulException(message) }

type ServerUnreachableException(message:string, innerException: Exception) =
   inherit ConnectionUnsuccessfulException (message, innerException)

module Networking =

    // Ubuntu 18.04 LTS still brings a very old version of Mono (4.6.2) that doesn't have TLS1.2 support
    let Tls12Support =
        let monoVersion = Config.GetMonoVersion()
        not (Option.exists (fun monoVersion -> monoVersion < Version("4.8")) monoVersion)

    let FindBuggyException (ex: Exception) (newExceptionMsg): Option<Exception> =
        let isOldMonoWithBuggyAsync =
            let monoVersion = Config.GetMonoVersion()
            Option.exists (fun monoVersion -> monoVersion < Version("5.0")) monoVersion
        let rec findBuggyExceptions (ex: Exception): Option<Exception> =
            if null = ex then
                None
            // see https://bugzilla.xamarin.com/show_bug.cgi?id=41133 | https://sentry.io/organizations/nblockchain/issues/918478485/
            elif ex.GetType() = typeof<FieldAccessException> then
                Some ex
            // see https://github.com/Microsoft/visualfsharp/issues/2720
            elif ex.GetType() = typeof<Exception> && ex.Message = "Unexpected no result" then
                Some ex
            else
                findBuggyExceptions ex.InnerException

        if not isOldMonoWithBuggyAsync then
            None
        else
            match findBuggyExceptions ex with
            | None -> None
            | Some buggyEx ->
                BuggyExceptionFromOldMonoVersion(newExceptionMsg, buggyEx) :> Exception |> Some

    let FindExceptionToRethrow (ex: Exception) (newExceptionMsg): Option<Exception> =
        let maybeSocketException = FSharpUtil.FindException<SocketException> ex
        match maybeSocketException with
        | None ->
            FindBuggyException ex newExceptionMsg
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
            elif socketException.ErrorCode = int SocketError.Shutdown then
                ServerClosedConnectionEarlyException(newExceptionMsg, ex) :> Exception |> Some

            else
                UnhandledSocketException(socketException.ErrorCode, ex) :> Exception |> Some

