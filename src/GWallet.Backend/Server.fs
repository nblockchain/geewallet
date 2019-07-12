namespace GWallet.Backend

open System

open Newtonsoft.Json

type ExceptionInfo =
    { TypeFullName: string
      Message: string }

type HistoryInfo =
    { TimeSpan: TimeSpan
      Fault: Option<ExceptionInfo> }

type Protocol =
    | Http
    | Tcp of port: uint32

type ConnectionType =
    {
        Encrypted: bool
        Protocol: Protocol
    }

type ServerDetails =
    {
        HostName: string
        ConnectionType: ConnectionType
        LastSuccessfulCommunication: Option<DateTime>
    }

module ServerRegistry =
    let Serialize(servers: seq<ServerDetails>): string =
        JsonConvert.SerializeObject
            (servers |> Seq.sortByDescending (fun s -> s.LastSuccessfulCommunication))

[<CustomEquality; NoComparison>]
type Server<'K,'R when 'K: equality> =
    { Identifier: 'K
      HistoryInfo: Option<HistoryInfo>
      Retrieval: Async<'R> }
    override self.Equals yObj =
        match yObj with
        | :? Server<'K,'R> as y ->
            self.Identifier.Equals y.Identifier
        | _ -> false
    override self.GetHashCode () =
        self.Identifier.GetHashCode()
