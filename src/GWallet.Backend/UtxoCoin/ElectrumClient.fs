﻿namespace GWallet.Backend.UtxoCoin

open System

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks

module ElectrumClient =

    let private Init (fqdn: string) (port: uint32) (timeouts: NetworkTimeouts): Async<StratumClient> =
        let jsonRpcClient = new JsonRpcTcpClient(fqdn, port, timeouts)
        let stratumClient = new StratumClient(jsonRpcClient)

        // this is the last version of Electrum released at the time of writing this module
        let CLIENT_NAME_SENT_TO_STRATUM_SERVER_WHEN_HELLO = Config.AppName

        // last version of the protocol [1] as of electrum's source code [2] at the time of
        // writing this... actually this changes relatively rarely (one of the last changes
        // was for 2.4 version [3] (changes documented here[4])
        // [1] https://electrumx-spesmilo.readthedocs.io/en/latest/protocol.html
        // [2] https://github.com/spesmilo/electrum/blob/master/lib/version.py
        // [3] https://github.com/spesmilo/electrum/commit/118052d81597eff3eb636d242eacdd0437dabdd6
        // [4] https://electrumx-spesmilo.readthedocs.io/en/latest/protocol-changes.html
        let PROTOCOL_VERSION_SUPPORTED = Version "1.4"

        async {
            let! versionSupportedByServer =
                try
                    stratumClient.ServerVersion CLIENT_NAME_SENT_TO_STRATUM_SERVER_WHEN_HELLO PROTOCOL_VERSION_SUPPORTED
                with
                | :? ElectrumServerReturningErrorException as ex ->
                    if (ex.ErrorCode = Some 1 && ex.Message.StartsWith "unsupported protocol version" &&
                                            ex.Message.EndsWith (PROTOCOL_VERSION_SUPPORTED.ToString())) then

                        // FIXME: even if this ex is already handled to ignore the server, we should report to sentry as WARN
                        raise <| ServerTooNewException(SPrintF1 "Version of server rejects our client version (%s)"
                                                             (PROTOCOL_VERSION_SUPPORTED.ToString()))
                    else
                        reraise()
            if versionSupportedByServer < PROTOCOL_VERSION_SUPPORTED then
                raise (ServerTooOldException (SPrintF2 "Version of server is older (%s) than the client (%s)"
                                                      (versionSupportedByServer.ToString())
                                                      (PROTOCOL_VERSION_SUPPORTED.ToString())))
            return stratumClient
        }

    let StratumServer (electrumServer: ServerDetails) (timeouts: NetworkTimeouts): Async<StratumClient> =
        match electrumServer.ServerInfo.ConnectionType with
        | { Encrypted = true; Protocol = _ } -> failwith "Incompatibility filter for non-encryption didn't work?"
        | { Encrypted = false; Protocol = Http } -> failwith "HTTP server for UtxoCoin?"
        | { Encrypted = false; Protocol = Tcp port } ->
            Init electrumServer.ServerInfo.NetworkPath port timeouts

    let GetBalances (scriptHashes: List<string>) (stratumServer: Async<StratumClient>) = async {
        // FIXME: we should rather implement this method in terms of:
        //        - querying all unspent transaction outputs (X) -> block heights included
        //        - querying transaction history (Y) -> block heights included
        //        - check the difference between X and Y (e.g. Y - X = Z)
        //        - query details of each element in Z to see their block heights
        //        - query the current blockheight (H) -> pick the highest among all servers queried
        //        -> having H, we now know which elements of X, Y, and Z are confirmed or not
        // Doing it this way has two advantages:
        // 1) We can configure GWallet with a number of confirmations to consider some balance confirmed (instead
        //    of trusting what "confirmed" means from the point of view of the Electrum Server)
        // 2) and most importantly: we could verify each of the transactions supplied in X, Y, Z to verify their
        //    integrity (in a similar fashion as Electrum Wallet client already does), to not have to trust servers*
        //    [ see https://www.youtube.com/watch?v=hjYCXOyDy7Y&feature=youtu.be&t=1171 for more information ]
        // * -> although that would be fixing only half of the problem, we also need proof of completeness
        let! stratumClient = stratumServer
        let rec innerGetBalances (scriptHashes: List<string>) (result: BlockchainScriptHashGetBalanceInnerResult) =
            async {
                match scriptHashes with
                | scriptHash::otherScriptHashes ->
                    let! balanceHash = stratumClient.BlockchainScriptHashGetBalance scriptHash
                    
                    return! 
                        innerGetBalances
                            otherScriptHashes
                            {
                                result with
                                    Unconfirmed = result.Unconfirmed + balanceHash.Result.Unconfirmed
                                    Confirmed = result.Confirmed + balanceHash.Result.Confirmed
                            }
                | [] ->
                    return result
            }

        return!
            innerGetBalances
                scriptHashes
                {
                    Unconfirmed = 0L
                    Confirmed = 0L
                }
    }

    let GetUnspentTransactionOutputs scriptHash (stratumServer: Async<StratumClient>) = async {
        let! stratumClient = stratumServer
        let! unspentListResult = stratumClient.BlockchainScriptHashListUnspent scriptHash
        return unspentListResult.Result
    }

    let GetBlockchainTransaction txHash (stratumServer: Async<StratumClient>) = async {
        let! stratumClient = stratumServer
        let! blockchainTransactionResult = stratumClient.BlockchainTransactionGet txHash
        return blockchainTransactionResult.Result
    }

    // DON'T DELETE, used in external projects
    let GetBlockchainTransactionIdFromPos (height: UInt32) (txPos: UInt32) (stratumServer: Async<StratumClient>) = async {
        let! stratumClient = stratumServer
        let! blockchainTransactionResult = stratumClient.BlockchainTransactionIdFromPos height txPos
        return blockchainTransactionResult.Result
    }

    let EstimateFee (numBlocksTarget: int) (stratumServer: Async<StratumClient>): Async<decimal> = async {
        let! stratumClient = stratumServer
        let! estimateFeeResult = stratumClient.BlockchainEstimateFee numBlocksTarget
        if estimateFeeResult.Result = -1m then
            return raise <| ServerMisconfiguredException("Fee estimation returned a -1 error code")
        elif estimateFeeResult.Result <= 0m then
            return raise <| ServerMisconfiguredException(SPrintF1 "Fee estimation returned an invalid non-positive value %M"
                                                                  estimateFeeResult.Result)

        let amountPerKB = estimateFeeResult.Result
        let satPerKB = (NBitcoin.Money (amountPerKB, NBitcoin.MoneyUnit.BTC)).ToUnit NBitcoin.MoneyUnit.Satoshi
        let satPerB = satPerKB / (decimal 1000)
        Infrastructure.LogDebug <| SPrintF2
            "Electrum server gave us a fee rate of %M per KB = %M sat per B" amountPerKB satPerB
        return amountPerKB
    }

    let BroadcastTransaction (transactionInHex: string) (stratumServer: Async<StratumClient>) = async {
        let! stratumClient = stratumServer
        let! blockchainTransactionBroadcastResult = stratumClient.BlockchainTransactionBroadcast transactionInHex
        return blockchainTransactionBroadcastResult.Result
    }

