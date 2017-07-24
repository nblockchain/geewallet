namespace GWallet.Backend.Bitcoin

open System
open System.Linq
open System.Text
open System.Net.Sockets

open Newtonsoft.Json
open Newtonsoft.Json.Serialization

open GWallet.Backend

exception ServerTooOld of string

type internal ElectrumClient (electrumServer: ElectrumServer) =
    let Init(stratumClient: StratumClient) =
        // this is the last version of Electrum released at the time of writing this module
        let CURRENT_ELECTRUM_FAKED_VERSION = Version("2.8.3")

        // last version of the protocol [1] as of electrum's source code [2] at the time of
        // writing this... actually this changes rarely, last change was for 2.4 version [3]
        // [1] http://docs.electrum.org/en/latest/protocol.html
        // [2] https://github.com/spesmilo/electrum/blob/master/lib/version.py
        // [3] https://github.com/spesmilo/electrum/commit/118052d81597eff3eb636d242eacdd0437dabdd6
        let PROTOCOL_VERSION_SUPPORTED = Version("0.10")

        let versionSupportedByServer = stratumClient.ServerVersion CURRENT_ELECTRUM_FAKED_VERSION PROTOCOL_VERSION_SUPPORTED
        if versionSupportedByServer < PROTOCOL_VERSION_SUPPORTED then
            raise (ServerTooOld (sprintf "Version of server is older (%s) than the client (%s)"
                                        (versionSupportedByServer.ToString()) (PROTOCOL_VERSION_SUPPORTED.ToString())))

    let jsonRpcClient = new JsonRpcSharp.Client(electrumServer.Host, electrumServer.Ports.InsecurePort.Value)
    let stratumClient = new StratumClient(jsonRpcClient)
    do Init(stratumClient)

    member self.GetBalance address: Int64 =
        let balanceResult = stratumClient.BlockchainAddressGetBalance address
        balanceResult.Result.Confirmed

    member self.GetUnspentTransactionOutputs address =
        let unspentListResult = stratumClient.BlockchainAddressListUnspent address
        unspentListResult.Result

    member self.GetBlockchainTransaction txHash =
        let blockchainTransactionResult = stratumClient.BlockchainTransactionGet txHash
        blockchainTransactionResult.Result

    member self.EstimateFee (numBlocksTarget: int): decimal =
        let estimateFeeResult = stratumClient.BlockchainEstimateFee numBlocksTarget
        estimateFeeResult.Result

    member self.BroadcastTransaction (transactionInHex: string) =
        let blockchainTransactionBroadcastResult = stratumClient.BlockchainTransactionBroadcast transactionInHex
        blockchainTransactionBroadcastResult.Result

    interface IDisposable with
        member x.Dispose() =
            (stratumClient:>IDisposable).Dispose()
