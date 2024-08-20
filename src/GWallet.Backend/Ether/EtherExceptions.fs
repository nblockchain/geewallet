namespace GWallet.Backend.Ether

open System
open System.Net
open System.Runtime.Serialization

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks

type HttpStatusCodeNotPresentInTheBcl =
    | TooManyRequests = 429
    | FrozenSite = 530

type RpcErrorCode =
    // so many different errors use this shitty error code... don't ask me why
    | JackOfAllTradesErrorCode = -32000

    // message was "rejected due to project ID settings" (don't ask me wtf this means)
    | ProjectIdSettingsRejection = -32002

    // ambiguous or generic because I've seen same code applied to two different error messages already:
    // "Transaction with the same hash was already imported. (Transaction with the same hash was already imported.)"
    // AND
    // "There are too many transactions in the queue. Your transaction was dropped due to limit.
    //  Try increasing the fee. (There are too many transactions in the queue. Your transaction was dropped due to
    //  limit. Try increasing the fee.)"
    | TransactionAlreadyImportedOrTooManyTransactionsInTheQueue = -32010

    | UnknownBlockNumber = -32602
    | GatewayTimeout = -32050
    | EmptyResponse = -32042
    | DailyRequestCountExceededSoRequestRateLimited = -32005
    | CannotFulfillRequest = -32046
    | ResourceNotFound = -32001
    | InternalError = -32603
    | UnparsableResponseType = -39000

type ServerCannotBeResolvedException =
    inherit CommunicationUnsuccessfulException

    new(message: string, innerException: Exception) =
        {
            inherit CommunicationUnsuccessfulException(message, innerException)
        }
    new (info: SerializationInfo, context: StreamingContext) =
        { inherit CommunicationUnsuccessfulException (info, context) }

type ServerUnavailableException =
    inherit CommunicationUnsuccessfulException

    new(message: string, innerException: Exception) =
        {
            inherit CommunicationUnsuccessfulException(message, innerException)
        }
    new (info: SerializationInfo, context: StreamingContext) =
        { inherit CommunicationUnsuccessfulException (info, context) }

type ServerChannelNegotiationException =
    inherit CommunicationUnsuccessfulException

    new(message: string, innerException: Exception) =
        {
            inherit CommunicationUnsuccessfulException(message, innerException)
        }
    new(message: string, webExStatusCode: WebExceptionStatus, innerException: Exception) =
        {
            inherit CommunicationUnsuccessfulException(SPrintF2 "%s (WebErr: %s)" message (webExStatusCode.ToString()),
                                                    innerException)
        }
    new(message: string, cloudFlareError: CloudFlareError, innerException: Exception) =
        {
            inherit CommunicationUnsuccessfulException(SPrintF2 "%s (CfErr: %s)" message (cloudFlareError.ToString()),
                                                    innerException)
        }
    new (info: SerializationInfo, context: StreamingContext) =
        { inherit CommunicationUnsuccessfulException (info, context) }

type ServerRestrictiveException =
    inherit CommunicationUnsuccessfulException

    new (message: string, innerException: Exception) =
        {
            inherit CommunicationUnsuccessfulException (message, innerException)
        }
    new (info: SerializationInfo, context: StreamingContext) =
        { inherit CommunicationUnsuccessfulException (info, context) }

type UnhandledWebException =
    inherit Exception

    new (status: WebExceptionStatus, innerException: Exception) =
        {
            inherit Exception (SPrintF1 "Backend not prepared for this WebException with Status[%i]"
                                       (int status),
                                       innerException)
        }
    new (info: SerializationInfo, context: StreamingContext) =
        { inherit Exception (info, context) }

/// Exception indicating that response JSON contains null value where it should not.
/// E.g. {"jsonrpc":"2.0","id":1,"result":null}
type AbnormalNullValueInJsonResponseException(message: string) =
    inherit CommunicationUnsuccessfulException(message)

    static member BalanceJobErrorMessage = "Abnormal null response from balance job"
