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

[<CustomEquality; NoComparison>]
type ServerDetails =
    {
        NetworkPath: string
        ConnectionType: ConnectionType
        CommunicationHistory: Option<HistoryInfo>
    }
    override self.Equals yObj =
        match yObj with
        | :? ServerDetails as y ->
            self.NetworkPath.Equals y.NetworkPath
        | _ -> false
    override self.GetHashCode () =
        self.NetworkPath.GetHashCode()
    interface ICommunicationHistory with
        member self.CommunicationHistory with get() = self.CommunicationHistory

module ServerRegistry =

    let ServersEmbeddedResourceFileName = "servers.json"

    let Serialize(servers: Map<Currency,seq<ServerDetails>>): string =
        let rec removeDupesInternal (servers: seq<ServerDetails>) (serversMap: Map<string,ServerDetails>) =
            match Seq.tryHead servers with
            | None -> Seq.empty
            | Some server ->
                let tail = Seq.tail servers
                match serversMap.TryGetValue server.NetworkPath with
                | false,_ ->
                    removeDupesInternal tail serversMap
                | true,serverInMap ->
                    let serverToAppend =
                        match server.CommunicationHistory,serverInMap.CommunicationHistory with
                        | None,_ -> serverInMap
                        | _,None -> server
                        | Some commHistory,Some commHistoryInMap ->
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
                    let newMap = serversMap.Remove serverToAppend.NetworkPath
                    Seq.append (seq { yield serverToAppend }) (removeDupesInternal tail newMap)

        let removeDupes (servers: seq<ServerDetails>)  =
            removeDupesInternal servers (servers |> Seq.map (fun server -> server.NetworkPath,server) |> Map.ofSeq)

        let sort (servers: seq<ServerDetails>) =
            Seq.sortByDescending (fun server ->
                                      match server.CommunicationHistory with
                                      | None -> None
                                      | Some history ->
                                          match history.Status with
                                          | Fault (_,lsc) -> lsc
                                          | LastSuccessfulCommunication lsc ->
                                              Some lsc
                                 ) servers

        let rearrangedServers =
            servers
            |> Map.toSeq
            |> Seq.map (fun (currency, servers) -> currency, servers |> removeDupes |> sort)
            |> Map.ofSeq
        Marshalling.Serialize rearrangedServers

    let Deserialize(json: string): Map<Currency,seq<ServerDetails>> =
        Marshalling.Deserialize json

    let internal servers = Deserialize (Config.ExtractEmbeddedResourceFileContents ServersEmbeddedResourceFileName)

    let GetServers currency =
        match servers.TryFind currency with
        | Some currencyServers -> currencyServers
        | _ -> failwithf "No servers found in resource file for %A?" currency

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
