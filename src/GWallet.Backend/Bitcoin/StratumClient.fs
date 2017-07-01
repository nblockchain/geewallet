namespace GWallet.Backend.Bitcoin

open System
open System.Linq
open System.Text
open System.Net.Sockets

open Newtonsoft.Json
open Newtonsoft.Json.Serialization

open GWallet.Backend

type private LowercaseContractResolver() =
    inherit DefaultContractResolver()
    override this.ResolvePropertyName (propertyName: string) =
        propertyName.ToLower()

// can't make this type below private, or else Newtonsoft.Json will serialize it incorrectly
type Request =
    {
        Id: int;
        Method: string;
        Params: seq<string>;
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

type ErrorResult =
    {
        Id: int;
        Error: string;
    }

type StratumClient (jsonRpcClient: JsonRpcSharp.Client) =
    let jsonSerializerSettings = new JsonSerializerSettings()
    do jsonSerializerSettings.ContractResolver <- new LowercaseContractResolver()

    static member private Deserialize<'T> (result: string, originalRequest: string): 'T =
        let maybeError = JsonConvert.DeserializeObject<ErrorResult>(result)
        if not (String.IsNullOrWhiteSpace(maybeError.Error)) then
            failwith (sprintf "Error received from Electrum server: '%s'. Original request sent from client: '%s'"
                              maybeError.Error originalRequest)
        JsonConvert.DeserializeObject<'T>(result)

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
        Version(resObj.Result)

    interface IDisposable with
        member x.Dispose() =
            (jsonRpcClient:>IDisposable).Dispose()
