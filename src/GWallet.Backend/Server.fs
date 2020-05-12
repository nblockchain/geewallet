namespace GWallet.Backend

open System

open GWallet.Backend.FSharpUtil

type ExceptionInfo =
    { TypeFullName: string
      Message: string }

type FaultInfo =
    {
        Exception: ExceptionInfo
        LastSuccessfulCommunication: Maybe<DateTime>
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
    abstract member CommunicationHistory: Maybe<HistoryInfo> with get

type HistoryFact =
    {
        TimeSpan: TimeSpan
        Fault: Maybe<ExceptionInfo>
    }

type ServerInfo =
    {
        NetworkPath: string
        ConnectionType: ConnectionType
    }

[<CustomEquality; NoComparison>]
type ServerDetails =
    {
        ServerInfo: ServerInfo
        CommunicationHistory: Maybe<CachedValue<HistoryInfo>>
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
                | Nothing -> Nothing
                | Just (h,_) -> Just h

type ServerRanking = Map<Currency,seq<ServerDetails>>

module ServerRegistry =

    let ServersEmbeddedResourceFileName = "servers.json"

    let internal TryFindValue (map: ServerRanking) (serverPredicate: ServerDetails -> bool)
                                  : Maybe<Currency*ServerDetails> =
        let rec tryFind currencyAndServers server =
            match currencyAndServers with
            | [] -> Nothing
            | (currency, servers)::tail ->
                match Seq.tryFind serverPredicate servers |> Maybe.OfOpt with
                | Nothing -> tryFind tail server
                | Just foundServer -> Just (currency, foundServer)
        let listMap = Map.toList map
        tryFind listMap serverPredicate

    let internal RemoveDupes (servers: seq<ServerDetails>) =
        let rec removeDupesInternal (servers: seq<ServerDetails>) (serversMap: Map<string,ServerDetails>) =
            match Seq.tryHead servers |> Maybe.OfOpt with
            | Nothing -> Seq.empty
            | Just server ->
                let tail = Seq.tail servers
                match serversMap.TryGetValue server.ServerInfo.NetworkPath with
                | false,_ ->
                    removeDupesInternal tail serversMap
                | true,serverInMap ->
                    let serverToAppend =
                        match server.CommunicationHistory,serverInMap.CommunicationHistory with
                        | Nothing,_ -> serverInMap
                        | _,Nothing -> server
                        | Just (_, lastComm),Just (_, lastCommInMap) ->
                            if lastComm > lastCommInMap then
                                server
                            else
                                serverInMap
                    let newMap = serversMap.Remove serverToAppend.ServerInfo.NetworkPath
                    Seq.append (seq { yield serverToAppend }) (removeDupesInternal tail newMap)

        let initialServersMap =
            servers
                |> Seq.map (fun server -> server.ServerInfo.NetworkPath, server)
                |> Map.ofSeq
        removeDupesInternal servers initialServersMap

    let internal RemoveBlackListed (cs: Currency*seq<ServerDetails>): seq<ServerDetails> =
        let isBlackListed currency server =
            // as these servers can only serve very limited set of queries (e.g. only balance?) their stats are skewed and
            // they create exception when being queried for advanced ones (e.g. latest block)
            server.ServerInfo.NetworkPath.Contains "blockscout" ||

            // there was a mistake when adding this server to geewallet's JSON: it was added in the ETC currency instead of ETH
            (currency = Currency.ETC && server.ServerInfo.NetworkPath.Contains "ethrpc.mewapi.io")

        let currency,servers = cs
        Seq.filter (fun server -> not (isBlackListed currency server)) servers

    let RemoveCruft (cs: Currency*seq<ServerDetails>): seq<ServerDetails> =
        cs |> RemoveBlackListed |> RemoveDupes

    let internal Sort (servers: seq<ServerDetails>): seq<ServerDetails> =
        let sort server =
            let invertOrder (timeSpan: TimeSpan): int =
                0 - int timeSpan.TotalMilliseconds
            match server.CommunicationHistory with
            | Nothing -> Nothing
            | Just (history, lastComm) ->
                match history.Status with
                | Fault faultInfo ->
                    let success = false
                    match faultInfo.LastSuccessfulCommunication with
                    | Nothing -> Just (success, invertOrder history.TimeSpan, Nothing)
                    | Just lsc -> Just (success, invertOrder history.TimeSpan, Just lsc)
                | Success ->
                    let success = true
                    Just (success, invertOrder history.TimeSpan, Just lastComm)

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
                    match ranking1.TryFind currency |> Maybe.OfOpt with
                    | Nothing -> Seq.empty
                    | Just servers -> servers
                let allServersFrom2 =
                    match ranking2.TryFind currency |> Maybe.OfOpt with
                    | Nothing -> Seq.empty
                    | Just servers ->
                        servers
                let allServers = (currency, Seq.append allServersFrom1 allServersFrom2)
                                 |> RemoveCruft
                                 |> Sort

                yield currency, allServers
        } |> Map.ofSeq

    let private ServersRankingBaseline =
        Deserialize (Config.ExtractEmbeddedResourceFileContents ServersEmbeddedResourceFileName)

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
