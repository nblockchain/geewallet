namespace GWallet.Backend.UtxoCoin

open System
open System.Text.RegularExpressions

open Newtonsoft.Json
open Newtonsoft.Json.Serialization

open GWallet.Backend

type private PascalCase2LowercasePlusUnderscoreContractResolver() =
    inherit DefaultContractResolver()

    // https://stackoverflow.com/a/20952003/544947
    let pascalToUnderScoreRegex = Regex("((?<=.)[A-Z][a-zA-Z]*)|((?<=[a-zA-Z])\d+)", RegexOptions.Multiline)
    let pascalToUnderScoreReplacementExpression = "_$1$2"
    override this.ResolvePropertyName (propertyName: string) =
        pascalToUnderScoreRegex.Replace(propertyName, pascalToUnderScoreReplacementExpression).ToLower()

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
        Result: string;
    }

type BlockchainAddressGetBalanceInnerResult =
    {
        Confirmed: Int64;
        Unconfirmed: Int64;
    }
type BlockchainAddressGetBalanceResult =
    {
        Id: int;
        Result: BlockchainAddressGetBalanceInnerResult;
    }

type BlockchainAddressListUnspentInnerResult =
    {
        TxHash: string;
        TxPos: int;
        Value: Int64;
        Height: Int64;
    }
type BlockchainAddressListUnspentResult =
    {
        Id: int;
        Result: BlockchainAddressListUnspentInnerResult array;
    }

type BlockchainTransactionGetResult =
    {
        Id: int;
        Result: string;
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

type public ElectrumServerReturningErrorInJsonResponseException(message: string, code: int) =
    inherit Exception(message)

    member val ErrorCode: int =
        code with get

type public ElectrumServerReturningErrorException(message: string, code: int, originalRequest: string) =
    inherit ElectrumServerReturningErrorInJsonResponseException(message, code)

    member val OriginalRequest: string =
        originalRequest with get

type public ElectrumServerReturningInternalErrorException(message: string, code: int, originalRequest: string) =
    inherit ElectrumServerReturningErrorException(message, code, originalRequest)

type StratumClient (jsonRpcClient: JsonRpcSharp.Client) =

    let jsonSerializerSettings = JsonSerializerSettings()
    do jsonSerializerSettings.ContractResolver <- PascalCase2LowercasePlusUnderscoreContractResolver()

    //FIXME: make the above and below converge into one, and that is only initialized once
    static member public GetDefaultJsonSerializationSettings(): JsonSerializerSettings =
        let jsonSerializerSettings = JsonSerializerSettings()
        jsonSerializerSettings.ContractResolver <- PascalCase2LowercasePlusUnderscoreContractResolver()
        jsonSerializerSettings

    // TODO: add 'T as incoming request type, leave 'R as outgoing response type
    member private self.Request<'R> (jsonRequest: string): 'R =
        try
            let rawResponse = jsonRpcClient.Request jsonRequest
            StratumClient.Deserialize<'R> rawResponse
        with
        | :? ElectrumServerReturningErrorInJsonResponseException as ex ->
            if (ex.ErrorCode = -32603) then
                raise(ElectrumServerReturningInternalErrorException(ex.Message, ex.ErrorCode, jsonRequest))
            raise(ElectrumServerReturningErrorException(ex.Message, ex.ErrorCode, jsonRequest))

    static member public Deserialize<'T> (result: string): 'T =
        let resultTrimmed = result.Trim()
        let maybeError =
            try
                JsonConvert.DeserializeObject<ErrorResult>(resultTrimmed, StratumClient.GetDefaultJsonSerializationSettings())
            with
            | ex -> raise(new Exception(sprintf "Failed deserializing JSON response (to check for error) '%s'" resultTrimmed, ex))

        if not (Object.ReferenceEquals(maybeError.Error, null)) then
            raise(ElectrumServerReturningErrorInJsonResponseException(maybeError.Error.Message, maybeError.Error.Code))

        try
            JsonConvert.DeserializeObject<'T>(resultTrimmed, StratumClient.GetDefaultJsonSerializationSettings())
        with
        | ex -> raise(new Exception(sprintf "Failed deserializing JSON response '%s'" resultTrimmed, ex))

    member self.BlockchainAddressGetBalance address: BlockchainAddressGetBalanceResult =
        let obj = {
            Id = 0;
            Method = "blockchain.address.get_balance";
            Params = [address]
        }
        let json = JsonConvert.SerializeObject(obj, Formatting.None, jsonSerializerSettings)

        self.Request<BlockchainAddressGetBalanceResult> json

    member self.ServerVersion (clientVersion: Version) (protocolVersion: Version): Version =
        let obj = {
            Id = 0;
            Method = "server.version";
            Params = [clientVersion.ToString(); protocolVersion.ToString()]
        }
        // this below serializes to:
        //  (sprintf "{ \"id\": 0, \"method\": \"server.version\", \"params\": [ \"%s\", \"%s\" ] }"
        //      CURRENT_ELECTRUM_FAKED_VERSION PROTOCOL_VERSION)
        let json = JsonConvert.SerializeObject(obj, Formatting.None, jsonSerializerSettings)
        let resObj = self.Request<ServerVersionResult> json

        // contradicting the spec, Result could contain "ElectrumX x.y.z.t" instead of just "x.y.z.t"
        let separatedBySpaces = resObj.Result.Split [|' '|]
        let version = separatedBySpaces.[separatedBySpaces.Length - 1]

        try
            Version(version)
        with
        | exn -> raise(Exception("Electrum Server's version disliked by .NET Version class: " + version, exn))

    member self.BlockchainAddressListUnspent address: BlockchainAddressListUnspentResult =
        let obj = {
            Id = 0;
            Method = "blockchain.address.listunspent";
            Params = [address]
        }
        let json = JsonConvert.SerializeObject(obj, Formatting.None, jsonSerializerSettings)
        let resObj = self.Request<BlockchainAddressListUnspentResult> json
        resObj

    member self.BlockchainTransactionGet txHash: BlockchainTransactionGetResult =
        let obj = {
            Id = 0;
            Method = "blockchain.transaction.get";
            Params = [txHash]
        }
        let json = JsonConvert.SerializeObject(obj, Formatting.None, jsonSerializerSettings)

        self.Request<BlockchainTransactionGetResult> json

    member self.BlockchainEstimateFee (numBlocksTarget: int): BlockchainEstimateFeeResult =
        let obj = {
            Id = 0;
            Method = "blockchain.estimatefee";
            Params = [numBlocksTarget]
        }
        let json = JsonConvert.SerializeObject(obj, Formatting.None, jsonSerializerSettings)

        self.Request<BlockchainEstimateFeeResult> json

    member self.BlockchainTransactionBroadcast txInHex: BlockchainTransactionBroadcastResult =
        let obj = {
            Id = 0;
            Method = "blockchain.transaction.broadcast";
            Params = [txInHex]
        }
        let json = JsonConvert.SerializeObject(obj, Formatting.None, jsonSerializerSettings)

        self.Request<BlockchainTransactionBroadcastResult> json

    interface IDisposable with
        member x.Dispose() =
            (jsonRpcClient:>IDisposable).Dispose()
