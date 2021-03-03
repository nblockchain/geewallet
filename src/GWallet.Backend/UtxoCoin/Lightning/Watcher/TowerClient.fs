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
        (quietMode: bool)
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
                    {
                        AddPunishmentTxRequest.TransactionHex = punishmentTx.ToHex()
                        CommitmentTxHash = commitments.RemoteCommit.TxId.Value.ToString() 
                    }

                do! self.AddPunishmentTx towerRequest
            with 
            | ex -> 
                if not quietMode then
                    raise <| FSharpUtil.ReRaise ex
        }


    member private self.AddPunishmentTx(request: AddPunishmentTxRequest): Async<unit> =
        async {
            use client = new TcpClient()

            do!
                client.ConnectAsync(self.TowerHost, self.TowerPort)
                |> Async.AwaitTask
            
            let mutable jsonRpc: JsonRpc = new JsonRpc(client.GetStream())
            jsonRpc.StartListening()

            return!
                jsonRpc.InvokeAsync("add_punishment_tx", request)
                |> Async.AwaitTask
        }

    member private self.GetRewardAddress(): Async<string> =
        async {
            use client = new TcpClient()

            do!
                client.ConnectAsync(self.TowerHost, self.TowerPort)
                |> Async.AwaitTask

            let mutable jsonRpc: JsonRpc = new JsonRpc(client.GetStream())
            jsonRpc.StartListening()

            return!
                jsonRpc.InvokeAsync<string>("get_reward_address")
                |> Async.AwaitTask
        }
