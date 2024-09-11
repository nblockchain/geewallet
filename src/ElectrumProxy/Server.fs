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

type ElectrumProxyServer() as self =
    static let blockchainHeadersSubscriptionInterval = TimeSpan.FromMinutes 1.0

    let blockchainHeadersSubscriptionEvent = new Event<UtxoCoin.BlockchainHeadersSubscribeInnerResult>()

    let cts = new Threading.CancellationTokenSource(-1)
    let blockchainHeadersSubscription = lazy(
        Async.Start(
            async {
                while true do
                    do! Async.Sleep blockchainHeadersSubscriptionInterval
                    let! blockchinTip = self.GetBlockchainTip()
                    blockchainHeadersSubscriptionEvent.Trigger blockchinTip
            }, cts.Token))
    
    interface IDisposable with
        override self.Dispose() =
            cts.Cancel()

    member self.EventNameTransform (name: string): string =
        match name with
        | "BlockchainHeadersSubscription" -> "blockchain.headers.subscribe"
        | _ -> name

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

    [<JsonRpcMethod("blockchain.block.headers")>]
    member self.BlockchainBlockHeaders (start_height: uint64) (count: uint64) : Task<UtxoCoin.BlockchainBlockHeadersInnerResult> =
        Query
            (fun asyncClient -> async {
                let! client = asyncClient
                let! result = client.BlockchainBlockHeaders start_height count
                return result.Result
            } )
        |> Async.StartAsTask

    [<JsonRpcMethod("blockchain.scripthash.get_history")>]
    member self.BlockchainScripthashGetHistory (scripthash: string) : Task<array<UtxoCoin.BlockchainScriptHashGetHistoryInnerResult>> =
        Query
            (fun asyncClient -> async {
                let! client = asyncClient
                let! result = client.BlockchainScriptHashGetHistory scripthash
                return result.Result
            } )
        |> Async.StartAsTask

    member private self.GetBlockchainTip() : Async<UtxoCoin.BlockchainHeadersSubscribeInnerResult> =
        Query
            (fun asyncClient -> async {
                let! client = asyncClient
                let! result = client.BlockchainHeadersSubscribe()
                return result.Result
            } )

    [<CLIEvent>]
    member this.BlockchainHeadersSubscription = blockchainHeadersSubscriptionEvent.Publish

    [<JsonRpcMethod("blockchain.headers.subscribe")>]
    member self.BlockchainHeadersSubscribe () : Task<UtxoCoin.BlockchainHeadersSubscribeInnerResult> =
        let task = self.GetBlockchainTip() |> Async.StartAsTask
        blockchainHeadersSubscription.Value
        task

    [<JsonRpcMethod("blockchain.transaction.get")>]
    member self.BlockchainTransactionGet (txHash: string) : Task<string> =
        Query
            (fun asyncClient -> async {
                let! client = asyncClient
                let! result = client.BlockchainTransactionGet txHash
                return result.Result
            } )
        |> Async.StartAsTask
