﻿namespace GWallet.Backend

open System
open System.Net
open System.Net.Sockets
open System.Runtime.Serialization

open Fsdk

open GWallet.Backend.FSharpUtil.UwpHacks

// https://en.wikipedia.org/wiki/List_of_HTTP_status_codes#Cloudflare
type CloudFlareError =
    | OriginError = 520
    | ConnectionTimeOut = 522
    | WebServerDown = 521
    | OriginUnreachable = 523
    | OriginSslHandshakeError = 525

type AbnormalSocketError =
    | CannotAssignRequestedAddress = 99
    | ResourceTemporarilyUnavailable = 11
    | NameOrServiceNotKnown = -131073
    | NoDataFoundForRequestedName = 11004

type internal UnhandledSocketException =
    inherit Exception

    new(socketErrorCode: int, innerException: Exception) =
        { inherit Exception(SPrintF1 "Backend not prepared for this SocketException with ErrorCode[%i]" socketErrorCode,
                                    innerException) }
    new (info: SerializationInfo, context: StreamingContext) =
        { inherit Exception (info, context) }

type CommunicationUnsuccessfulException =
    inherit Exception

    new(message: string, innerException: Exception) = { inherit Exception(message, innerException) }
    new(message: string) = { inherit Exception(message) }
    new() = { inherit Exception() }
    new (info: SerializationInfo, context: StreamingContext) =
        { inherit Exception (info, context) }

type ServerDiscardedException =
   inherit Exception

   new (message: string, innerException: Exception) =
       { inherit Exception (message, innerException) }
   new (info: SerializationInfo, context: StreamingContext) =
       { inherit Exception (info, context) }

type BuggyExceptionFromOldMonoVersion =
    inherit CommunicationUnsuccessfulException

    new (message: string, innerException: Exception) =
        { inherit CommunicationUnsuccessfulException (message, innerException) }
    new (info: SerializationInfo, context: StreamingContext) =
        { inherit CommunicationUnsuccessfulException (info, context) }

type ServerClosedConnectionEarlyException =
    inherit CommunicationUnsuccessfulException

    new (message: string, innerException: Exception) =
        { inherit CommunicationUnsuccessfulException (message, innerException) }
    new (info: SerializationInfo, context: StreamingContext) =
        { inherit CommunicationUnsuccessfulException (info, context) }

type ServerRefusedException =
    inherit CommunicationUnsuccessfulException

    new (message: string, innerException: Exception) =
        { inherit CommunicationUnsuccessfulException (message, innerException) }
    new (info: SerializationInfo, context: StreamingContext) =
        { inherit CommunicationUnsuccessfulException (info, context) }

type ServerTimedOutException =
    inherit CommunicationUnsuccessfulException

    new(message: string, innerException: Exception) =
        { inherit CommunicationUnsuccessfulException(message, innerException) }
    new(message) =
        { inherit CommunicationUnsuccessfulException(message) }
    new (info: SerializationInfo, context: StreamingContext) =
        { inherit CommunicationUnsuccessfulException (info, context) }

type ServerUnreachableException =
    inherit CommunicationUnsuccessfulException

    new(message: string, innerException: Exception) =
        {
            inherit CommunicationUnsuccessfulException(message, innerException)
        }
    new(message: string, httpStatusCode: HttpStatusCode, innerException: Exception) =
        {
            inherit CommunicationUnsuccessfulException(SPrintF2 "%s (HttpErr: %s)" message (httpStatusCode.ToString()),
                                                    innerException)
        }
    new(message: string, cloudFlareError: CloudFlareError, innerException: Exception) =
        {
            inherit CommunicationUnsuccessfulException(SPrintF2 "%s (CfErr: %s)" message (cloudFlareError.ToString()),
                                                    innerException)
        }
    new (info: SerializationInfo, context: StreamingContext) =
        { inherit CommunicationUnsuccessfulException (info, context) }

type ServerFaultException =
    inherit CommunicationUnsuccessfulException

    new (message: string, innerException: Exception) =
        { inherit CommunicationUnsuccessfulException (message, innerException) }

type ServerMisconfiguredException =
    inherit CommunicationUnsuccessfulException

    new (message: string, innerException: Exception) =
        { inherit CommunicationUnsuccessfulException (message, innerException) }
    new (message: string) =
        { inherit CommunicationUnsuccessfulException (message) }
    new(message: string, httpStatusCode: HttpStatusCode, innerException: Exception) =
        {
            inherit CommunicationUnsuccessfulException(SPrintF2 "%s (HttpErr: %s)" message (httpStatusCode.ToString()),
                                                                innerException)
        }
    new (info: SerializationInfo, context: StreamingContext) =
        { inherit CommunicationUnsuccessfulException (info, context) }

module Networking =

    let QueryRestApi (uri: Uri) =
        async {
            use webClient = new WebClient()
            try
                let task = webClient.DownloadStringTaskAsync uri
                let! result = Async.AwaitTask task
                return Some result
            with
            | ex ->
                if (FSharpUtil.FindException<WebException> ex).IsSome then
                    return None
                else
                    return raise <| FSharpUtil.ReRaise ex
        }

    let FindExceptionToRethrow (ex: Exception) (newExceptionMsg): Option<Exception> =
        match FSharpUtil.FindException<SocketException> ex with
        | None ->
            None
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

            // -1!?! WTF, mono bug in v6.4.0? see https://sentry.io/organizations/nblockchain/issues/1261821968/
            elif socketException.ErrorCode = int SocketError.SocketError &&
                 socketException.Message.Contains "mono-io-layer-error" then
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
            elif socketException.ErrorCode = int SocketError.ProtocolOption then
                ServerUnreachableException(newExceptionMsg, ex) :> Exception |> Some
            elif socketException.ErrorCode = int SocketError.HostNotFound then
                ServerUnreachableException(newExceptionMsg, ex) :> Exception |> Some

            // FYI these 3 socketErrors below we got them (in the span of less than a day) when upgrading to .NET6:
            elif socketException.ErrorCode = int AbnormalSocketError.ResourceTemporarilyUnavailable then
                ServerRefusedException(newExceptionMsg, ex) :> Exception |> Some
            elif socketException.ErrorCode = int AbnormalSocketError.NameOrServiceNotKnown then
                ServerUnreachableException(newExceptionMsg, ex) :> Exception |> Some
            elif socketException.ErrorCode = int AbnormalSocketError.NoDataFoundForRequestedName then
                ServerMisconfiguredException(newExceptionMsg, ex) :> Exception |> Some

            // we mark it as "buggy from old mono" because this sounds like it would be fixed in .NET6
            // (the version when Xamarin will work the .NETCore BCL)
            elif socketException.ErrorCode = int SocketError.InvalidArgument then
                BuggyExceptionFromOldMonoVersion(newExceptionMsg, ex) :> Exception |> Some

            elif socketException.ErrorCode = int AbnormalSocketError.CannotAssignRequestedAddress then
                ServerUnreachableException(newExceptionMsg, ex) :> Exception |> Some

            else
                UnhandledSocketException(socketException.ErrorCode, ex) :> Exception |> Some

