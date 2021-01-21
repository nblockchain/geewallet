namespace GWallet.Backend.UtxoCoin.Lightning.Watcher

open System.Net.Sockets
open StreamJsonRpc
open GWallet.Backend.UtxoCoin
open DotNetLightning.Utils
open DotNetLightning.Channel
open NBitcoin
open GWallet.Backend.UtxoCoin.Lightning
open GWallet.Backend

type internal TowerClient =
    { TowerHost: string
      TowerPort: int }

    static member Default =
        { 
            TowerHost = Config.DEFAULT_WATCHTOWER_HOST
            TowerPort = Config.DEFAULT_WATCHTOWER_PORT
        }
               
    member internal self.CreateAndSendPunishmentTx
        (perCommitmentSecret: PerCommitmentSecret)
        (commitments: Commitments)
        (localChannelPrivKeys: ChannelPrivKeys)
        (network: Network)
        (account: NormalUtxoAccount)
        : Async<unit> =
        async {
            try 
                let! rewardAdderss = self.GetRewardAddress()

                let! punishmentTx =
                    ForceCloseTransaction.CreatePunishmentTx
                        perCommitmentSecret
                        commitments
                        localChannelPrivKeys
                        network
                        account
                        (rewardAdderss |> Some)

                let towerRequest =
                    { AddPunishmentTxRequest.TransactionHex = punishmentTx.ToHex()
                      CommitmentTxHash = commitments.RemoteCommit.TxId.Value.ToString() }

                do! self.AddPunishmentTx towerRequest |> Async.Ignore //for now we ignore the response beacuse we have no way of handling it
            with
            | :? System.Exception -> ignore //for now we ignore the response beacuse we have no way of handling it
        }


    member private self.AddPunishmentTx(request: AddPunishmentTxRequest): Async<AddPunishmentTxResponse> =
        async {
            use client = new TcpClient()

            do!
                client.ConnectAsync(self.TowerHost, self.TowerPort)
                |> Async.AwaitTask

            use jsonRpc: JsonRpc = new JsonRpc(client.GetStream())
            jsonRpc.StartListening()

            return!
                jsonRpc.InvokeAsync<AddPunishmentTxResponse>("add_punishment_tx", request)
                |> Async.AwaitTask
        }

    member private self.GetRewardAddress(): Async<string> =
        async {
            use client = new TcpClient()

            do!
                client.ConnectAsync(self.TowerHost, self.TowerPort)
                |> Async.AwaitTask

            use jsonRpc: JsonRpc = new JsonRpc(client.GetStream())
            jsonRpc.StartListening()

            return!
                jsonRpc.InvokeAsync<string>("get_reward_address")
                |> Async.AwaitTask
        }
