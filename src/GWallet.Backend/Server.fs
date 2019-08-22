namespace GWallet.Backend

open System

type ExceptionInfo =
    { TypeFullName: string
      Message: string }

type LastSuccessfulCommunicationTime = DateTime

type Status =
    | Fault of ExceptionInfo*Option<LastSuccessfulCommunicationTime>
    | LastSuccessfulCommunication of LastSuccessfulCommunicationTime

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

    let internal RemoveDupes (servers: seq<ServerDetails>) =
        let rec removeDupesInternal (servers: seq<ServerDetails>) (serversMap: Map<string,ServerDetails>) =
            match Seq.tryHead servers with
            | None -> Seq.empty
            | Some server ->
                let tail = Seq.tail servers
                match serversMap.TryGetValue server.ServerInfo.NetworkPath with
                | false,_ ->
                    removeDupesInternal tail serversMap
                | true,serverInMap ->
                    let serverToAppend =
                        match server.CommunicationHistory,serverInMap.CommunicationHistory with
                        | None,_ -> serverInMap
                        | _,None -> server
                        | Some (commHistory,_),Some (commHistoryInMap,_) ->
                            match commHistory.Status,commHistoryInMap.Status with
                            | Fault(_,None),_ -> serverInMap
                            | _,Fault(_,None) -> server
                            | LastSuccessfulCommunication lsc,LastSuccessfulCommunication lscInMap
                            | LastSuccessfulCommunication lsc,Fault(_,Some lscInMap)
                            | Fault(_,Some lsc),LastSuccessfulCommunication lscInMap
                            | Fault(_,Some lsc),Fault(_,Some lscInMap) ->
                                if lsc > lscInMap then
                                    server
                                else
                                    serverInMap
                    let newMap = serversMap.Remove serverToAppend.ServerInfo.NetworkPath
                    Seq.append (seq { yield serverToAppend }) (removeDupesInternal tail newMap)

        removeDupesInternal servers
                            (servers |> Seq.map (fun server -> server.ServerInfo.NetworkPath,server) |> Map.ofSeq)

    let internal Sort (servers: seq<ServerDetails>): seq<ServerDetails> =
        Seq.sortByDescending (fun server ->
                                  let invertOrder (timeSpan: TimeSpan): int =
                                      0 - int timeSpan.TotalMilliseconds
                                  match server.CommunicationHistory with
                                  | None -> None
                                  | Some (history,_) ->
                                      match history.Status with
                                      | Fault (_,maybeLsc) ->
                                          let success = false
                                          match maybeLsc with
                                          | None -> Some (success, invertOrder history.TimeSpan, None)
                                          | Some lsc -> Some (success, invertOrder history.TimeSpan, Some lsc)
                                      | LastSuccessfulCommunication lsc ->
                                          let success = true
                                          Some (success, invertOrder history.TimeSpan, Some lsc)
                             ) servers

    let Serialize(servers: ServerRanking): string =
        let rearrangedServers =
            servers
            |> Map.toSeq
            |> Seq.map (fun (currency, servers) -> currency, servers |> RemoveDupes |> Sort)
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
                yield currency,((Seq.append allServersFrom1 allServersFrom2) |> RemoveDupes |> Sort)
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
