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

type ElectrumProxyServer() =
    [<JsonRpcMethod("server.version")>]
    member self.ServerVersion (_clientVersion: string) (_protocolVersion: string) = 
        supportedProtocolVersion

    [<JsonRpcMethod("server.ping")>]
    member self.ServerPing () = ()

    [<JsonRpcMethod("blockchain.block.header")>]
    member self.BlockchainBlockHeader (height: uint64) : Task<string> =
        Query
            (fun asyncClient -> async {
                let! client = asyncClient
                let! result = client.BlockchainBlockHeader height
                return result.Result
            } )
        |> Async.StartAsTask
