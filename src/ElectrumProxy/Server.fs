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

let ScriptHashToAddress (scriptHash: string) =
    let scriptId = NBitcoin.WitScriptId scriptHash
    scriptId.GetAddress NBitcoin.Network.Main

let private QueryElectrum<'R when 'R: equality> (job: Async<UtxoCoin.StratumClient>->Async<'R>) : Async<'R> =
    UtxoCoin.Server.Query Currency.BTC (UtxoCoin.QuerySettings.Default ServerSelectionMode.Fast) job None

let private QueryMultiple<'R when 'R: equality> 
    (electrumJob: Async<UtxoCoin.StratumClient>->Async<'R>) 
    (additionalServers: List<Server<ServerDetails,'R>>) : Async<'R> =
    let updateServer serverMatchFunc stat =
        if additionalServers |> List.exists (fun each -> serverMatchFunc each.Details) |> not then
            Caching.Instance.SaveServerLastStat serverMatchFunc stat
        
    let faultTolerantClient =
        FaultTolerantParallelClient<ServerDetails,ServerDiscardedException> updateServer
    let query = faultTolerantClient.Query 
    let querySettings = UtxoCoin.Server.FaultTolerantParallelClientDefaultSettings ServerSelectionMode.Fast None
    query
        querySettings
        (List.append
            (UtxoCoin.Server.GetRandomizedFuncs Currency.BTC electrumJob)
            additionalServers)

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

    let bitcoreNodeAddress = "https://api.bitcore.io"
    let bitcoreNodeClient = new BitcoreNodeClient(bitcoreNodeAddress)

    // Cache results of "blockchain.scripthash.get_history" requests. Invalidate cache only when
    // new block(s) are added to the blockchain.
    let mutable blockchainHeight = 0UL
    let mutable scripthashHistoryCache = Map.empty<string, array<UtxoCoin.BlockchainScriptHashGetHistoryInnerResult>>
    
    interface IDisposable with
        override self.Dispose() =
            (bitcoreNodeClient :> IDisposable).Dispose()
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
        QueryElectrum
            (fun asyncClient -> async {
                let! client = asyncClient
                let! result = client.BlockchainBlockHeader height
                return result.Result
            } )
        |> Async.StartAsTask

    [<JsonRpcMethod("blockchain.block.headers")>]
    member self.BlockchainBlockHeaders (start_height: uint64) (count: uint64) : Task<UtxoCoin.BlockchainBlockHeadersInnerResult> =
        QueryElectrum
            (fun asyncClient -> async {
                let! client = asyncClient
                let! result = client.BlockchainBlockHeaders start_height count
                return result.Result
            } )
        |> Async.StartAsTask

    [<JsonRpcMethod("blockchain.scripthash.get_history")>]
    member self.BlockchainScripthashGetHistory (scripthash: string) : Task<array<UtxoCoin.BlockchainScriptHashGetHistoryInnerResult>> =
        let electrumJob = 
            (fun (asyncClient: Async<UtxoCoin.StratumClient>) -> async {
                let! client = asyncClient
                let! result = client.BlockchainScriptHashGetHistory scripthash
                return result.Result
            } )
        let bitcoreNodeServer: Server<ServerDetails, array<UtxoCoin.BlockchainScriptHashGetHistoryInnerResult>> =
            {
                Details = { 
                    ServerInfo = { 
                        NetworkPath = bitcoreNodeAddress
                        ConnectionType = { ConnectionType.Encrypted = true; Protocol = Protocol.Http } 
                    } 
                    CommunicationHistory = None
                }
                Retrieval = async {
                    let address = ScriptHashToAddress scripthash
                    return! bitcoreNodeClient.GetAddressTransactions (address.ToString())
                }
            }
            
        async {
            match scripthashHistoryCache |> Map.tryFind scripthash with
            | Some value -> return value
            | None ->
                let! result = 
                    QueryMultiple
                        electrumJob
                        (List.singleton bitcoreNodeServer)
                lock 
                    scripthashHistoryCache 
                    (fun () -> scripthashHistoryCache <- scripthashHistoryCache |> Map.add scripthash result)
                return result
        }
        |> Async.StartAsTask

    member private self.GetBlockchainTip() : Async<UtxoCoin.BlockchainHeadersSubscribeInnerResult> =
        QueryElectrum
            (fun asyncClient -> async {
                let! client = asyncClient
                let! result = client.BlockchainHeadersSubscribe()
                let height = result.Result.Height
                if height > blockchainHeight then
                    blockchainHeight <- height
                    lock
                        scripthashHistoryCache
                        (fun () -> scripthashHistoryCache <- Map.empty)
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
        QueryElectrum
            (fun asyncClient -> async {
                let! client = asyncClient
                let! result = client.BlockchainTransactionGet txHash
                return result.Result
            } )
        |> Async.StartAsTask

    [<JsonRpcMethod("blockchain.transaction.broadcast")>]
    member self.BlockchainTransactionBroadcast (rawTx: string) : Task<string> =
        QueryElectrum
            (fun asyncClient -> async {
                let! client = asyncClient
                let! result = client.BlockchainTransactionBroadcast rawTx
                return result.Result
            } )
        |> Async.StartAsTask
