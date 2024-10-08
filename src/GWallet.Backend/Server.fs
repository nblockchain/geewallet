namespace GWallet.Backend

open System

type ExceptionInfo =
    { TypeFullName: string
      Message: string }

type FaultInfo =
    {
        Exception: ExceptionInfo
        LastSuccessfulCommunication: Option<DateTime>
    }

type Status =
    | Fault of FaultInfo
    | Success

type HistoryInfo =
    { TimeSpan: TimeSpan
      Status: Status }

type Protocol =
    | Http
    | Tcp of port: uint32

type ConnectionType =
    {
        Encrypted: bool
        Protocol: Protocol
    }

type ICommunicationHistory =
    abstract member CommunicationHistory: Option<HistoryInfo> with get

type HistoryFact =
    {
        TimeSpan: TimeSpan
        Fault: Option<ExceptionInfo>
    }

type ServerInfo =
    {
        NetworkPath: string
        ConnectionType: ConnectionType
    }

// FIXME: create type 'CurrencyServer' that is a record of Currency*ServerDetails which is rather used instead
// of ServerDetails, so that functions that use a server also know which currency they're dealing with (this
// way we can, for example, retry if NoneAvailable exception in case ETC is used, cause there's a lack of servers
// in that ecosystem at the moment)

[<CustomEquality; NoComparison>]
type ServerDetails =
    {
        ServerInfo: ServerInfo
        CommunicationHistory: Option<CachedValue<HistoryInfo>>
    }
    member private self.EqualsInternal (yObj: obj) =
        match yObj with
        | :? ServerDetails as y ->
            self.ServerInfo.Equals y.ServerInfo
        | _ -> false
    override self.Equals yObj =
        self.EqualsInternal yObj
    override self.GetHashCode () =
        self.ServerInfo.GetHashCode()
    interface ICommunicationHistory with
        member self.CommunicationHistory
            with get() =
                match self.CommunicationHistory with
                | None -> None
                | Some (h,_) -> Some h

type ServerRanking = Map<Currency,seq<ServerDetails>>

module ServerRegistry =

    let ServersEmbeddedResourceFileName = "servers.json"

    let internal TryFindValue (map: ServerRanking) (serverPredicate: ServerDetails -> bool)
                                  : Option<Currency*ServerDetails> =
        let rec tryFind currencyAndServers server =
            match currencyAndServers with
            | [] -> None
            | (currency, servers)::tail ->
                match Seq.tryFind serverPredicate servers with
                | None -> tryFind tail server
                | Some foundServer -> Some (currency, foundServer)
        let listMap = Map.toList map
        tryFind listMap serverPredicate

    let AddServer (newServer: ServerDetails) (servers: seq<ServerDetails>) : seq<ServerDetails> =
        let serversArray = Seq.toArray servers
        match Array.tryFindIndex (fun each -> each.ServerInfo.NetworkPath = newServer.ServerInfo.NetworkPath) serversArray with
        | Some index ->
            let existingServer = serversArray.[index]
            match newServer.CommunicationHistory, existingServer.CommunicationHistory with
            | None, _ -> ()
            | _, None -> 
                serversArray.[index] <- newServer
            | Some (_, newLastComm),Some (_, existingLastCommInMap) when newLastComm > existingLastCommInMap ->
                serversArray.[index] <- newServer
            | _ -> ()
            serversArray :> seq<ServerDetails>
        | None -> 
            Array.append serversArray (Array.singleton newServer) :> seq<ServerDetails>

    let internal RemoveBlackListed (cs: Currency*seq<ServerDetails>): seq<ServerDetails> =
        let isBlackListed currency server =
            // as these servers can only serve very limited set of queries (e.g. only balance?) their stats are skewed and
            // they create exception when being queried for advanced ones (e.g. latest block)
            server.ServerInfo.NetworkPath.Contains "blockscout" ||

            // there was a mistake when adding this server to our servers.json file: it was added in the ETC currency instead of ETH
            (currency = Currency.ETC && server.ServerInfo.NetworkPath.Contains "ethrpc.mewapi.io")

            // there was a typo when adding this server to our servers.json file, see commit 69d90fd2fc22a1f3dd9ef8793f0cd42e3b540df1
            || (currency = Currency.ETC && server.ServerInfo.NetworkPath.Contains "ethercluster.comx/")

        let currency,servers = cs
        Seq.filter (fun server -> not (isBlackListed currency server)) servers

    let RemoveCruft (cs: Currency*seq<ServerDetails>): seq<ServerDetails> =
        cs |> RemoveBlackListed

    let internal Sort (servers: seq<ServerDetails>): seq<ServerDetails> =
        let sort server =
            let invertOrder (timeSpan: TimeSpan): int =
                0 - int timeSpan.TotalMilliseconds
            match server.CommunicationHistory with
            | None -> None
            | Some (history, lastComm) ->
                match history.Status with
                | Fault faultInfo ->
                    let success = false
                    match faultInfo.LastSuccessfulCommunication with
                    | None -> Some (success, invertOrder history.TimeSpan, None)
                    | Some lsc -> Some (success, invertOrder history.TimeSpan, Some lsc)
                | Success ->
                    let success = true
                    Some (success, invertOrder history.TimeSpan, Some lastComm)

        Seq.sortByDescending sort servers

    let Serialize(servers: ServerRanking): string =
        let rearrangedServers =
            servers
            |> Map.toSeq
            |> Seq.map (fun (currency, servers) -> currency, ((currency,servers) |> RemoveCruft |> Sort))
            |> Map.ofSeq
        Marshalling.Serialize rearrangedServers

    let Deserialize(json: string): ServerRanking =
        Marshalling.Deserialize json

    let Merge (ranking1: ServerRanking) (ranking2: ServerRanking): ServerRanking =
        let allKeys =
            seq {
                for KeyValue(key, _) in ranking1 do
                    yield key
                for KeyValue(key, _) in ranking2 do
                    yield key
            } |> Set.ofSeq

        seq {
            for currency in allKeys do
                let allServersFrom1 =
                    match ranking1.TryFind currency with
                    | None -> Seq.empty
                    | Some servers -> servers
                let allServersFrom2 =
                    match ranking2.TryFind currency with
                    | None -> Seq.empty
                    | Some servers ->
                        servers
                let mergedServers = 
                    Seq.fold 
                        (fun servers newServer -> AddServer newServer servers)
                        allServersFrom1
                        allServersFrom2
                let allServers = (currency, mergedServers)
                                 |> RemoveCruft
                                 |> Sort

                yield currency, allServers
        } |> Map.ofSeq

    let private ServersRankingBaseline =
        Deserialize (Fsdk.Misc.ExtractEmbeddedResourceFileContents ServersEmbeddedResourceFileName)

    let MergeWithBaseline (ranking: ServerRanking): ServerRanking =
        Merge ranking ServersRankingBaseline

[<CustomEquality; NoComparison>]
type Server<'K,'R when 'K: equality and 'K :> ICommunicationHistory> =
    { Details: 'K
      Retrieval: Async<'R> }
    override self.Equals yObj =
        match yObj with
        | :? Server<'K,'R> as y ->
            self.Details.Equals y.Details
        | _ -> false
    override self.GetHashCode () =
        self.Details.GetHashCode()
