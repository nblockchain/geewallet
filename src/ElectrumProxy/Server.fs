module Server

open System
open System.Text

open StreamJsonRpc

type PascalCaseToSnakeCaseNamingPolicy() = 
    inherit Json.JsonNamingPolicy()

    static let capitalizedWordRegex = RegularExpressions.Regex "[A-Z][a-z0-9]*"

    override self.ConvertName name =
        let evaluator (regexMatch: RegularExpressions.Match) =
            let lowercase = regexMatch.Value.ToLower()
            if regexMatch.Index = 0 then lowercase else "_" + lowercase
        capitalizedWordRegex.Replace(name, Text.RegularExpressions.MatchEvaluator evaluator)

let supportedProtocolVersion = "0.10"

let AddMethods (jsonRpc: JsonRpc) =
    jsonRpc.AddLocalRpcMethod(
        "server.version", 
        new Func<string, string, string>(fun _clientVersion _protocolVersion -> supportedProtocolVersion))
    jsonRpc.AddLocalRpcMethod(
        "server.ping", 
        new Func<unit>(fun () -> ()))
