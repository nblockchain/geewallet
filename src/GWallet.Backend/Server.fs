namespace GWallet.Backend

open System

open Newtonsoft.Json

type ExceptionInfo =
    { TypeFullName: string
      Message: string }

type LastSuccessfulCommunication = DateTime

type Status =
    | Fault of ExceptionInfo*Option<LastSuccessfulCommunication>
    | LastSuccessfulCommunication of LastSuccessfulCommunication

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
        HostName: string
        ConnectionType: ConnectionType
        CommunicationHistory: Option<HistoryInfo>
    }
    override self.Equals yObj =
        match yObj with
        | :? ServerDetails as y ->
            self.HostName.Equals y.HostName
        | _ -> false
    override self.GetHashCode () =
        self.HostName.GetHashCode()
    interface ICommunicationHistory with
        member self.CommunicationHistory with get() = self.CommunicationHistory

module ServerRegistry =
    let Serialize(servers: Map<Currency,seq<ServerDetails>>): string =
        let sort servers =
            Seq.sortByDescending (fun server ->
                                      match server.CommunicationHistory with
                                      | None -> None
                                      | Some history ->
                                          match history.Status with
                                          | Fault (_,lsc) -> lsc
                                          | LastSuccessfulCommunication lsc ->
                                              Some lsc
                                 ) servers
        let sortedServers =
            servers
            |> Map.toSeq
            |> Seq.map (fun (currency, servers) -> currency, sort servers)
            |> Map.ofSeq
        JsonConvert.SerializeObject sortedServers

    let Deserialize(json: string): Map<Currency,seq<ServerDetails>> =
        JsonConvert.DeserializeObject<Map<Currency,seq<ServerDetails>>> json

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
