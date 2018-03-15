namespace GWallet.Backend.UtxoCoin

open System

open GWallet.Backend

exception ServerTooOld of string

type internal ElectrumClient (electrumServer: ElectrumServer) =
    let Init(): StratumClient =
        if electrumServer.UnencryptedPort.IsNone then
            raise(JsonRpcSharp.TlsNotSupportedYetInGWalletException())

        let jsonRpcClient = new JsonRpcSharp.Client(electrumServer.Fqdn, electrumServer.UnencryptedPort.Value)
        let stratumClient = new StratumClient(jsonRpcClient)

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
        stratumClient

    let stratumClient = Init()

    member self.GetBalance address =
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
        let balanceResult = stratumClient.BlockchainAddressGetBalance address
        balanceResult.Result

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
