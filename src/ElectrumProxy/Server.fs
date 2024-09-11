module Server

open System
open System.Text
open System.Threading.Tasks

open StreamJsonRpc

open GWallet.Backend

type PascalCaseToSnakeCaseNamingPolicy() = 
    inherit Json.JsonNamingPolicy()

    static let capitalizedWordRegex = RegularExpressions.Regex "[A-Z][a-z0-9]*"

    override self.ConvertName name =
        let evaluator (regexMatch: RegularExpressions.Match) =
            let lowercase = regexMatch.Value.ToLower()
            if regexMatch.Index = 0 then lowercase else "_" + lowercase
        capitalizedWordRegex.Replace(name, Text.RegularExpressions.MatchEvaluator evaluator)

let supportedProtocolVersion = "1.3"

let private Query<'R when 'R: equality> (job: Async<UtxoCoin.StratumClient>->Async<'R>) : Async<'R> =
    UtxoCoin.Server.Query Currency.BTC (UtxoCoin.QuerySettings.Default ServerSelectionMode.Fast) job None

let AddMethods (jsonRpc: JsonRpc) =
    jsonRpc.AddLocalRpcMethod(
        "server.version", 
        new Func<string, string, string>(fun _clientVersion _protocolVersion -> supportedProtocolVersion))
    jsonRpc.AddLocalRpcMethod(
        "server.ping", 
        new Func<unit>(fun () -> ()))
    jsonRpc.AddLocalRpcMethod(
        "blockchain.block.header", 
        new Func<uint64, Task<string>>(
            fun height -> 
                Query 
                    (fun asyncClient -> async {
                        let! client = asyncClient
                        let! result = client.BlockchainBlockHeader height
                        return result.Result
                    } )
                |> Async.StartAsTask
            ))
