namespace GWallet.Regtest

open Newtonsoft.Json // For JsonConvert

open System
open System.IO // For File.WriteAllText

open NBitcoin // For ExtKey

open DotNetLightning.Utils
open ResultUtils.Portability
open GWallet.Backend.FSharpUtil.UwpHacks

type Bitcoind = {
    DataDir: string
    RpcUser: string
    RpcPassword: string
    ProcessWrapper: ProcessWrapper
} with
    interface IDisposable with
        member this.Dispose() =
            this.ProcessWrapper.Process.Kill()
            this.ProcessWrapper.WaitForExit()
            Directory.Delete(this.DataDir, true)

    static member Start(): Async<Bitcoind> = async {
        let dataDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
        Directory.CreateDirectory dataDir |> ignore
        let rpcUser = Path.GetRandomFileName()
        let rpcPassword = Path.GetRandomFileName()
        let confPath = Path.Combine(dataDir, "bitcoin.conf")
        (*
        File.WriteAllText(
            confPath,
            SPrintF2
                "\
                txindex=1\n\
                printtoconsole=1\n\
                rpcuser=%s\n\
                rpcpassword=%s\n\
                rpcallowip=127.0.0.1\n\
                zmqpubrawblock=tcp://127.0.0.1:28332\n\
                zmqpubrawtx=tcp://127.0.0.1:28333\n\
                fallbackfee=0.00001\n\
                [regtest]\n\
                rpcbind=127.0.0.1\n\
                rpcport=18554"
                rpcUser
                rpcPassword
        )
        *)
        File.WriteAllText(
            confPath,
            "\
            txindex=1\n\
            printtoconsole=1\n\
            rpcallowip=127.0.0.1\n\
            zmqpubrawblock=tcp://127.0.0.1:28332\n\
            zmqpubrawtx=tcp://127.0.0.1:28333\n\
            fallbackfee=0.00001\n\
            [regtest]\n\
            rpcbind=127.0.0.1\n\
            rpcport=18554"
        )

        let processWrapper =
            ProcessWrapper.New
                "bitcoind"
                (SPrintF1 "-regtest -datadir=%s" dataDir)
                Map.empty
                false
        processWrapper.WaitForMessage (fun msg -> msg.EndsWith "init message: Done loading")
        do! Async.Sleep 2000
        return {
            DataDir = dataDir
            RpcUser = rpcUser
            RpcPassword = rpcPassword
            ProcessWrapper = processWrapper
        }
    }

    member this.GenerateBlocksToBurnAddress (number: BlockHeightOffset32) =
        let address =
            let key = new Key()
            key.PubKey.GetScriptAddress(Network.RegTest)
        this.GenerateBlocks number address

    member this.GenerateBlocks (number: BlockHeightOffset32) (address: BitcoinAddress) =
        let bitcoinCli =
            ProcessWrapper.New
                "bitcoin-cli"
                (SPrintF3 "-regtest -datadir=%s generatetoaddress %i %s" this.DataDir number.Value (address.ToString()))
                Map.empty
                false
        bitcoinCli.WaitForExit()

    member this.GetTxIdsInMempool(): list<TxId> =
        let bitcoinCli =
            ProcessWrapper.New
                "bitcoin-cli"
                (SPrintF1 "-regtest -datadir=%s getrawmempool" this.DataDir)
                Map.empty
                false
        let lines = bitcoinCli.ReadToEnd()
        let output = String.concat "\n" lines
        let txIdList = JsonConvert.DeserializeObject<list<string>> output
        List.map (fun (txIdString: string) -> TxId <| uint256 txIdString) txIdList

    member this.RpcAddr(): string =
        "127.0.0.1:18554"

    member this.RpcUrl(): string =
        SPrintF3 "http://%s:%s@%s" this.RpcUser this.RpcPassword (this.RpcAddr())

