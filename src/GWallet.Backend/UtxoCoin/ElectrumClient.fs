namespace GWallet.Backend.UtxoCoin

open System

open GWallet.Backend
open System.Runtime.ExceptionServices

type ElectrumClient (electrumServer: ElectrumServer) =

    let Init(): Async<StratumClient> =
        electrumServer.CheckCompatibility()

        let jsonRpcClient = new JsonRpcTcpClient(electrumServer.Fqdn, electrumServer.UnencryptedPort.Value)
        let stratumClient = new StratumClient(jsonRpcClient)

        // this is the last version of Electrum released at the time of writing this module
        let CLIENT_NAME_SENT_TO_STRATUM_SERVER_WHEN_HELLO = "geewallet"

        // last version of the protocol [1] as of electrum's source code [2] at the time of
        // writing this... actually this changes rarely, last change was for 2.4 version [3]
        // (changes documented here[4])
        // [1] https://electrumx.readthedocs.io/en/latest/protocol.html
        // [2] https://github.com/spesmilo/electrum/blob/master/lib/version.py
        // [3] https://github.com/spesmilo/electrum/commit/118052d81597eff3eb636d242eacdd0437dabdd6
        // [4] https://electrumx.readthedocs.io/en/latest/protocol-changes.html
        let PROTOCOL_VERSION_SUPPORTED = Version("1.1")

        async {
            let! versionSupportedByServer =
                try
                    stratumClient.ServerVersion CLIENT_NAME_SENT_TO_STRATUM_SERVER_WHEN_HELLO PROTOCOL_VERSION_SUPPORTED
                with
                | :? ElectrumServerReturningErrorException as ex ->
                    if (ex.ErrorCode = 1 && ex.Message.StartsWith "unsupported protocol version" &&
                                            ex.Message.EndsWith (PROTOCOL_VERSION_SUPPORTED.ToString())) then

                        // FIXME: even if this ex is already handled to ignore the server, we should report to sentry as WARN
                        raise (ServerTooNewException(sprintf "Version of server rejects our client version (%s)"
                                                             (PROTOCOL_VERSION_SUPPORTED.ToString())))
                    else
                        reraise()
            if versionSupportedByServer < PROTOCOL_VERSION_SUPPORTED then
                raise (ServerTooOldException (sprintf "Version of server is older (%s) than the client (%s)"
                                                      (versionSupportedByServer.ToString())
                                                      (PROTOCOL_VERSION_SUPPORTED.ToString())))
            return stratumClient
        }

    let stratumClientAsync = Init()

    member self.GetBalance address = async {
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
            let! stratumClient = stratumClientAsync
            let! balanceResult = stratumClient.BlockchainAddressGetBalance address
            return balanceResult.Result
        }

    member self.GetUnspentTransactionOutputs address = async {
        let! stratumClient = stratumClientAsync
        let! unspentListResult = stratumClient.BlockchainAddressListUnspent address
        return unspentListResult.Result
    }

    member self.GetBlockchainTransaction txHash = async {
        let! stratumClient = stratumClientAsync
        let! blockchainTransactionResult = stratumClient.BlockchainTransactionGet txHash
        return blockchainTransactionResult.Result
    }

    member self.EstimateFee (numBlocksTarget: int): Async<decimal> = async {
        let! stratumClient = stratumClientAsync
        let! estimateFeeResult = stratumClient.BlockchainEstimateFee numBlocksTarget
        return estimateFeeResult.Result
    }

    member self.BroadcastTransaction (transactionInHex: string) = async {
        let! stratumClient = stratumClientAsync
        let! blockchainTransactionBroadcastResult = stratumClient.BlockchainTransactionBroadcast transactionInHex
        return blockchainTransactionBroadcastResult.Result
    }

