namespace GWallet.Backend.Bitcoin

open System
open System.Linq
open System.Text
open System.Text.RegularExpressions
open System.Net.Sockets

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

type ErrorResult =
    {
        Id: int;
        Error: string;
    }

type StratumClient (jsonRpcClient: JsonRpcSharp.Client) =
    let jsonSerializerSettings = JsonSerializerSettings()
    do jsonSerializerSettings.ContractResolver <- PascalCase2LowercasePlusUnderscoreContractResolver()

    //FIXME: make the above and below converge into one, and that is only initialized once
    static member private GetDefaultJsonSerializationSettings(): JsonSerializerSettings =
        let jsonSerializerSettings = JsonSerializerSettings()
        jsonSerializerSettings.ContractResolver <- PascalCase2LowercasePlusUnderscoreContractResolver()
        jsonSerializerSettings

    static member private Deserialize<'T> (result: string, originalRequest: string): 'T =
        let resultTrimmed = result.Trim()
        let maybeError =
            try
                JsonConvert.DeserializeObject<ErrorResult>(resultTrimmed, StratumClient.GetDefaultJsonSerializationSettings())
            with
            | ex -> raise(new Exception(sprintf "Failed deserializing JSON response (to check for error) '%s'" resultTrimmed, ex))

        if not (String.IsNullOrWhiteSpace(maybeError.Error)) then
            failwith (sprintf "Error received from Electrum server: '%s'. Original request sent from client: '%s'"
                              maybeError.Error originalRequest)
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

        let res = jsonRpcClient.Request json
        let resObj = StratumClient.Deserialize<BlockchainAddressGetBalanceResult>(res, json)
        resObj

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
        let res = jsonRpcClient.Request json
        let resObj = StratumClient.Deserialize<ServerVersionResult>(res, json)

        // contradicting the spec, Result could contain "ElectrumX x.y.z.t" instead of just "x.y.z.t"
        let separatedBySpaces = resObj.Result.Split [|' '|]
        let version = separatedBySpaces.[separatedBySpaces.Length - 1]

        Version(version)

    member self.BlockchainAddressListUnspent address: BlockchainAddressListUnspentResult =
        let obj = {
            Id = 0;
            Method = "blockchain.address.listunspent";
            Params = [address]
        }
        let json = JsonConvert.SerializeObject(obj, Formatting.None, jsonSerializerSettings)
        let res = jsonRpcClient.Request json
        let resObj = StratumClient.Deserialize<BlockchainAddressListUnspentResult>(res, json)
        resObj

    member self.BlockchainTransactionGet txHash: BlockchainTransactionGetResult =
        let obj = {
            Id = 0;
            Method = "blockchain.transaction.get";
            Params = [txHash]
        }
        let json = JsonConvert.SerializeObject(obj, Formatting.None, jsonSerializerSettings)

        let res = jsonRpcClient.Request json
        let resObj = StratumClient.Deserialize<BlockchainTransactionGetResult>(res, json)
        resObj

    member self.BlockchainEstimateFee (numBlocksTarget: int): BlockchainEstimateFeeResult =
        let obj = {
            Id = 0;
            Method = "blockchain.estimatefee";
            Params = [numBlocksTarget]
        }
        let json = JsonConvert.SerializeObject(obj, Formatting.None, jsonSerializerSettings)

        let res = jsonRpcClient.Request json
        let resObj = StratumClient.Deserialize<BlockchainEstimateFeeResult>(res, json)
        resObj

    member self.BlockchainTransactionBroadcast txInHex: BlockchainTransactionBroadcastResult =
        let obj = {
            Id = 0;
            Method = "blockchain.transaction.broadcast";
            Params = [txInHex]
        }
        let json = JsonConvert.SerializeObject(obj, Formatting.None, jsonSerializerSettings)

        let res = jsonRpcClient.Request json
        let resObj = StratumClient.Deserialize<BlockchainTransactionBroadcastResult>(res, json)
        resObj

    interface IDisposable with
        member x.Dispose() =
            (jsonRpcClient:>IDisposable).Dispose()
