namespace GWallet.Backend.Ether

open System
open System.Net

open GWallet.Backend

type HttpStatusCodeNotPresentInTheBcl =
    | TooManyRequests = 429

type RpcErrorCode =
    // "This request is not supported because your node is running with state pruning. Run with --pruning=archive."
    | StatePruningNode = -32000

    // ambiguous or generic because I've seen same code applied to two different error messages already:
    // "Transaction with the same hash was already imported. (Transaction with the same hash was already imported.)"
    // AND
    // "There are too many transactions in the queue. Your transaction was dropped due to limit.
    //  Try increasing the fee. (There are too many transactions in the queue. Your transaction was dropped due to
    //  limit. Try increasing the fee.)"
    | AmbiguousOrGenericError = -32010

    | UnknownBlockNumber = -32602
    | GatewayTimeout = -32050


type ServerCannotBeResolvedException =
    inherit CommunicationUnsuccessfulException

    new(message: string, innerException: Exception) =
        {
            inherit CommunicationUnsuccessfulException(message, innerException)
        }

type ServerUnavailableException =
    inherit CommunicationUnsuccessfulException

    new(message: string, innerException: Exception) =
        {
            inherit CommunicationUnsuccessfulException(message, innerException)
        }

type ServerChannelNegotiationException =
    inherit CommunicationUnsuccessfulException

    new(message: string, innerException: Exception) =
        {
            inherit CommunicationUnsuccessfulException(message, innerException)
        }
    new(message: string, webExStatusCode: WebExceptionStatus, innerException: Exception) =
        {
            inherit CommunicationUnsuccessfulException(sprintf "%s (WebErr: %s)" message (webExStatusCode.ToString()),
                                                    innerException)
        }
    new(message: string, cloudFlareError: CloudFlareError, innerException: Exception) =
        {
            inherit CommunicationUnsuccessfulException(sprintf "%s (CfErr: %s)" message (cloudFlareError.ToString()),
                                                    innerException)
        }

type ServerRestrictiveException =
    inherit CommunicationUnsuccessfulException

    new (message: string, innerException: Exception) =
        {
            inherit CommunicationUnsuccessfulException (message, innerException)
        }

type UnhandledWebException =
    inherit Exception

    new (status: WebExceptionStatus, innerException: Exception) =
        {
            inherit Exception (sprintf "Backend not prepared for this WebException with Status[%d]"
                                       (int status),
                                       innerException)
        }
