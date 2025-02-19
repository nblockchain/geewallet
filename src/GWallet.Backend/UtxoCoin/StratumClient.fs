namespace GWallet.Backend.UtxoCoin

open System
open System.ComponentModel

open Newtonsoft.Json

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks

// can't make this type below private, or else Newtonsoft.Json will serialize it incorrectly
type Request =
    {
        Id: int;
        Method: string;
        Params: seq<obj>;
    }

type ServerVersionResult =
    {
        Id: int;
        Result: array<string>;
    }

type BlockchainScriptHashGetBalanceInnerResult =
    {
        Confirmed: Int64;
        Unconfirmed: Int64;
    }
type BlockchainScriptHashGetBalanceResult =
    {
        Id: int;
        Result: BlockchainScriptHashGetBalanceInnerResult
    }

type BlockchainScriptHashListUnspentInnerResult =
    {
        TxHash: string;
        TxPos: int;
        Value: Int64;
        Height: Int64;
    }
type BlockchainScriptHashListUnspentResult =
    {
        Id: int;
        Result: array<BlockchainScriptHashListUnspentInnerResult>
    }

type BlockchainTransactionGetResult =
    {
        Id: int;
        Result: string;
    }

// DON'T DELETE, used in external projects
type BlockchainTransactionIdFromPosResult =
    {
        Id: int
        Result: string
    }

type BlockchainEstimateFeeResult =
    {
        Id: int;
        Result: decimal;
    }

type BlockchainTransactionBroadcastResult =
    {
        Id: int;
        Result: string;
    }

type BlockchainBlockHeaderResult =
    {
        Id: int;
        Result: string;
    }

type BlockchainBlockHeadersInnerResult =
    {
        Count: uint64
        Hex: string
        Max: uint64
    }

type BlockchainBlockHeadersResult =
    {
        Id: int;
        Result: BlockchainBlockHeadersInnerResult;
    }

type BlockchainScriptHashGetHistoryInnerResult =
    {
        Height: uint64
        TxHash: string
    }

type BlockchainScriptHashGetHistoryResult =
    {
        Id: int;
        Result: array<BlockchainScriptHashGetHistoryInnerResult>;
    }

type BlockchainHeadersSubscribeInnerResult =
    {
        Height: uint64
        Hex: string
    }

type BlockchainHeadersSubscribeResult =
    {
        Id: int;
        Result: BlockchainHeadersSubscribeInnerResult;
    }

type ErrorInnerResult =
    {
        Message: string;
        Code: int;
    }

type ErrorResult =
    {
        Id: int;
        Error: ErrorInnerResult;
    }

type ErrorResultWithStringError =
    {
        Id: int
        Error: string
    }

type RpcErrorCode =
    // see https://gitlab.com/nblockchain/geewallet/issues/110
    | ExcessiveResourceUsage = -101

    // see https://gitlab.com/nblockchain/geewallet/issues/117
    | ServerBusy = -102

    // see git commit msg of 0aba03a8291daa526fde888d0c02a789abe411f2
    | InternalError = -32603

    // see https://gitlab.com/nblockchain/geewallet/issues/112
    | UnknownMethod = -32601

type public ElectrumServerReturningImproperJsonResponseException(message: string, innerEx: Exception) =
    inherit ServerMisconfiguredException (message, innerEx)

type public ElectrumServerReturningErrorInJsonResponseException(message: string, code: Option<int>) =
    inherit CommunicationUnsuccessfulException(message)

    member val ErrorCode: Option<int> =
        code with get

type public ElectrumServerReturningErrorException(message: string, code: Option<int>,
                                                  originalRequest: string, originalResponse: string) =
    inherit ElectrumServerReturningErrorInJsonResponseException(message, code)

    member val OriginalRequest: string =
        originalRequest with get

    member val OriginalResponse: string =
        originalResponse with get

type public ElectrumServerReturningInternalErrorException(message: string, code: Option<int>,
                                                          originalRequest: string, originalResponse: string) =
    inherit ElectrumServerReturningErrorException(message, code, originalRequest, originalResponse)

type StratumClient (jsonRpcClient: JsonRpcTcpClient) =

    let Serialize(req: Request): string =
        JsonConvert.SerializeObject(req, Formatting.None,
                                    Marshalling.PascalCase2LowercasePlusUnderscoreConversionSettings)

    // TODO: add 'T as incoming request type, leave 'R as outgoing response type
    member private self.Request<'R> (jsonRequest: string): Async<'R*string> = async {
        let! rawResponse = jsonRpcClient.Request jsonRequest

        // FIXME: we should actually fix this bug in JsonRpcSharp (https://github.com/nblockchain/JsonRpcSharp/issues/9)
        if String.IsNullOrEmpty rawResponse then
            return raise <| ProtocolGlitchException(SPrintF2 "Server '%s' returned a null/empty JSON response to the request '%s'??"
                                                             jsonRpcClient.Host jsonRequest)

        try
            return (StratumClient.Deserialize<'R> rawResponse, rawResponse)
        with
        | :? ElectrumServerReturningErrorInJsonResponseException as ex ->
            if ex.ErrorCode = (RpcErrorCode.InternalError |> int |> Some) then
                return raise(ElectrumServerReturningInternalErrorException(ex.Message, ex.ErrorCode, jsonRequest, rawResponse))
            if ex.ErrorCode = (RpcErrorCode.UnknownMethod |> int |> Some) then
                return raise <| ServerMisconfiguredException(ex.Message, ex)
            if ex.ErrorCode = (RpcErrorCode.ServerBusy |> int |> Some) then
                return raise <| ServerFaultException(ex.Message, ex)
            if ex.ErrorCode = (RpcErrorCode.ExcessiveResourceUsage |> int |> Some) then
                return raise <| ServerFaultException(ex.Message, ex)

            return raise(ElectrumServerReturningErrorException(ex.Message, ex.ErrorCode, jsonRequest, rawResponse))
    }

    static member private DeserializeInternal<'T> (result: string): 'T =
        let resultTrimmed = result.Trim()

        let maybeError: Choice<ErrorResult, ErrorResultWithStringError> =
            let raiseDeserializationError (ex: Exception) =
                raise <| Exception(SPrintF2 "Failed deserializing JSON response (to check for error) '%s' to type '%s'"
                                                   resultTrimmed typedefof<'T>.FullName, ex)
            try
                JsonConvert.DeserializeObject<ErrorResult>(resultTrimmed,
                                                           Marshalling.PascalCase2LowercasePlusUnderscoreConversionSettings)
                |> Choice1Of2
            with
            | :? JsonSerializationException ->
                try
                    JsonConvert.DeserializeObject<ErrorResultWithStringError>(resultTrimmed,
                                                               Marshalling.PascalCase2LowercasePlusUnderscoreConversionSettings)
                    |> Choice2Of2
                with
                | ex ->
                    raiseDeserializationError ex
            | ex ->
                raiseDeserializationError ex

        match maybeError with
        | Choice1Of2 errorResult when (not (Object.ReferenceEquals(errorResult, null))) && (not (Object.ReferenceEquals(errorResult.Error, null)))  ->
            raise <| ElectrumServerReturningErrorInJsonResponseException(errorResult.Error.Message, Some errorResult.Error.Code)
        | Choice2Of2 errorResultWithStringError when (not (Object.ReferenceEquals(errorResultWithStringError, null))) && (not (String.IsNullOrWhiteSpace errorResultWithStringError.Error)) ->
            raise <| ElectrumServerReturningErrorInJsonResponseException(errorResultWithStringError.Error, None)
        | _ -> ()

        let failedDeserMsg = SPrintF2 "Failed deserializing JSON response '%s' to type '%s'"
                                      resultTrimmed typedefof<'T>.FullName
        let deserializedValue =
            try
                JsonConvert.DeserializeObject<'T>(resultTrimmed,
                                                  Marshalling.PascalCase2LowercasePlusUnderscoreConversionSettings)
            with
            | :? Newtonsoft.Json.JsonSerializationException as serEx ->
                let newEx = ElectrumServerReturningImproperJsonResponseException(failedDeserMsg, serEx)
#if !DEBUG
                Infrastructure.ReportWarning newEx
                |> ignore<bool>
#endif
                raise newEx
            | ex -> raise <| Exception(failedDeserMsg, ex)

        if Object.ReferenceEquals(deserializedValue, null) then
            failwith <| SPrintF2 "Failed deserializing JSON response '%s' to type '%s' (result was null)"
                      resultTrimmed typedefof<'T>.FullName

        deserializedValue

    // TODO: should this validation actually be part of JsonRpcSharp?
    static member public Deserialize<'T> (result: string): 'T =
        match Marshalling.IsValidJson result with
        | false ->
            raise <| ServerMisconfiguredException(SPrintF1 "Server's reply was not valid json: %s" result)
        | true ->
            StratumClient.DeserializeInternal result

    member self.BlockchainBlockHeader (height: uint64): Async<BlockchainBlockHeaderResult> =
        let obj = {
            Id = 0;
            Method = "blockchain.block.header";
            Params = [height]
        }
        let json = Serialize obj

        async {
            let! resObj,_ = self.Request<BlockchainBlockHeaderResult> json
            return resObj
        }

    member self.BlockchainBlockHeaders (start_height: uint64) (count: uint64): Async<BlockchainBlockHeadersResult> =
        let obj = {
            Id = 0;
            Method = "blockchain.block.headers";
            Params = [start_height; count]
        }
        let json = Serialize obj

        async {
            let! resObj,_ = self.Request<BlockchainBlockHeadersResult> json
            return resObj
        }

    member self.BlockchainHeadersSubscribe (): Async<BlockchainHeadersSubscribeResult> =
        let obj = {
            Id = 0;
            Method = "blockchain.headers.subscribe";
            Params = []
        }
        let json = Serialize obj

        async {
            let! resObj,_ = self.Request<BlockchainHeadersSubscribeResult> json
            return resObj
        }

    member self.BlockchainScriptHashGetBalance address: Async<BlockchainScriptHashGetBalanceResult> =
        let obj = {
            Id = 0;
            Method = "blockchain.scripthash.get_balance";
            Params = [address]
        }
        let json = Serialize obj

        async {
            let! resObj,_ = self.Request<BlockchainScriptHashGetBalanceResult> json
            return resObj
        }

    member self.BlockchainScriptHashGetHistory address: Async<BlockchainScriptHashGetHistoryResult> =
        let obj = {
            Id = 0;
            Method = "blockchain.scripthash.get_history";
            Params = [address]
        }
        let json = Serialize obj

        async {
            let! resObj,_ = self.Request<BlockchainScriptHashGetHistoryResult> json
            return resObj
        }

    static member private CreateVersion(versionStr: string): Version =
        let correctedVersion =
            if (versionStr.EndsWith("+")) then
                versionStr.Substring(0, versionStr.Length - 1)
            else
                versionStr
        try
            Version(correctedVersion)
        with
        | exn -> raise(Exception("Electrum Server's version disliked by .NET Version class: " + versionStr, exn))

    member self.ServerVersion (clientName: string) (protocolVersion: Version): Async<Version> = async {
        let obj = {
            Id = 0;
            Method = "server.version";
            Params = [clientName; protocolVersion.ToString()]
        }
        // this below serializes to:
        //  (SPrintF2 "{ \"id\": 0, \"method\": \"server.version\", \"params\": [ \"%s\", \"%s\" ] }"
        //      CURRENT_ELECTRUM_FAKED_VERSION PROTOCOL_VERSION)
        let json = Serialize obj
        let! resObj, rawResponse = self.Request<ServerVersionResult> json

        if Object.ReferenceEquals (resObj, null) then
            failwith <| SPrintF1 "resObj is null?? raw response was %s" rawResponse

        if Object.ReferenceEquals (resObj.Result, null) then
            failwith <| SPrintF1 "resObj.Result is null?? raw response was %s" rawResponse

        // resObj.Result.[0] is e.g. "ElectrumX 1.4.3"
        // e.g. "1.1"
        let serverProtocolVersion = resObj.Result.[1]

        return StratumClient.CreateVersion(serverProtocolVersion)
    }

    member self.BlockchainScriptHashListUnspent address: Async<BlockchainScriptHashListUnspentResult> =
        let obj = {
            Id = 0;
            Method = "blockchain.scripthash.listunspent";
            Params = [address]
        }
        let json = Serialize obj
        async {
            let! resObj,_ = self.Request<BlockchainScriptHashListUnspentResult> json
            return resObj
        }

    member self.BlockchainTransactionGet txHash: Async<BlockchainTransactionGetResult> =
        let obj = {
            Id = 0;
            Method = "blockchain.transaction.get";
            Params = [txHash]
        }
        let json = Serialize obj
        async {
            let! resObj,_ = self.Request<BlockchainTransactionGetResult> json
            return resObj
        }

    // DON'T DELETE, used in external projects
    member self.BlockchainTransactionIdFromPos height txPos: Async<BlockchainTransactionIdFromPosResult> =
        let obj = {
            Id = 0;
            Method = "blockchain.transaction.id_from_pos";
            Params = [height :> obj; txPos :> obj]
        }
        let json = Serialize obj
        async {
            let! resObj,_ = self.Request<BlockchainTransactionIdFromPosResult> json
            return resObj
        }

    // NOTE: despite Electrum-X official docs claiming that this method is deprecated... it's not! go read the official
    //       non-shitcoin forked version of the docs: https://electrumx-spesmilo.readthedocs.io/en/latest/protocol-methods.html#blockchain-estimatefee
    member self.BlockchainEstimateFee (numBlocksTarget: int): Async<BlockchainEstimateFeeResult> =
        let obj = {
            Id = 0;
            Method = "blockchain.estimatefee";
            Params = [numBlocksTarget]
        }
        let json = Serialize obj

        async {
            let! resObj,_ = self.Request<BlockchainEstimateFeeResult> json
            return resObj
        }

    member self.BlockchainTransactionBroadcast txInHex: Async<BlockchainTransactionBroadcastResult> =
        let obj = {
            Id = 0;
            Method = "blockchain.transaction.broadcast";
            Params = [txInHex]
        }
        let json = Serialize obj

        async {
            let! resObj,_ = self.Request<BlockchainTransactionBroadcastResult> json
            return resObj
        }
