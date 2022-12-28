namespace GWallet.Backend.Tests.End2End

open System
open System.IO

open Newtonsoft.Json
open NBitcoin
open DotNetLightning.Utils

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks

type Bitcoind = {
    DataDir: string
    RpcUser: string
    RpcPassword: string
    ProcessWrapper: ProcessWrapper
} with
    interface IDisposable with
        member self.Dispose() =
            Infrastructure.LogDebug "About to kill bitcoind process..."
            self.ProcessWrapper.Process.Kill()
            self.ProcessWrapper.WaitForExit()
            Directory.Delete(self.DataDir, true)

    static member Start(): Async<Bitcoind> = async {
        let dataDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
        Directory.CreateDirectory dataDir |> ignore
        Console.WriteLine(sprintf "*** line %s of %s" __LINE__ __SOURCE_FILE__)
        let rpcUser = Path.GetRandomFileName()
        let rpcPassword = Path.GetRandomFileName()
        let confPath = Path.Combine(dataDir, "bitcoin.conf")
        let fakeFeeRate = UtxoCoin.ElectrumClient.RegTestFakeFeeRate
        Console.WriteLine(sprintf "*** line %s of %s" __LINE__ __SOURCE_FILE__)
        File.WriteAllText(
            confPath,
            SPrintF1
                "\
                txindex=1\n\
                printtoconsole=1\n\
                rpcallowip=127.0.0.1\n\
                zmqpubrawblock=tcp://127.0.0.1:28332\n\
                zmqpubrawtx=tcp://127.0.0.1:28333\n\
                fallbackfee=%f\n\
                [regtest]\n\
                rpcbind=127.0.0.1\n\
                rpcport=18554"
                fakeFeeRate
        )
        Console.WriteLine(sprintf "*** line %s of %s" __LINE__ __SOURCE_FILE__)

        let processWrapper =
            ProcessWrapper.New
                "bitcoind"
                (SPrintF1 "-regtest -datadir=%s" dataDir)
                Map.empty
                false
        Console.WriteLine(sprintf "*** line %s of %s" __LINE__ __SOURCE_FILE__)
        processWrapper.WaitForMessage (fun msg -> msg.EndsWith "init message: Done loading")
        Console.WriteLine(sprintf "*** line %s of %s" __LINE__ __SOURCE_FILE__)

        do! Async.Sleep 2000
        Console.WriteLine(sprintf "*** line %s of %s" __LINE__ __SOURCE_FILE__)

        return {
            DataDir = dataDir
            RpcUser = rpcUser
            RpcPassword = rpcPassword
            ProcessWrapper = processWrapper
        }
    }

    member self.GenerateBlocks (number: BlockHeightOffset32) (address: BitcoinAddress) =
        let bitcoinCli =
            ProcessWrapper.New
                "bitcoin-cli"
                (SPrintF3 "-regtest -datadir=%s generatetoaddress %i %s" self.DataDir number.Value (address.ToString()))
                Map.empty
                false
        bitcoinCli.WaitForExit()

    member this.GenerateBlocksToDummyAddress (number: BlockHeightOffset32) =
        let address =
            let key = new Key()
            key.PubKey.GetAddress(ScriptPubKeyType.Segwit, Network.RegTest)
        this.GenerateBlocks number address

    member self.GetTxIdsInMempool(): list<TxId> =
        let bitcoinCli =
            ProcessWrapper.New
                "bitcoin-cli"
                (SPrintF1 "-regtest -datadir=%s getrawmempool" self.DataDir)
                Map.empty
                false
        Console.WriteLine(sprintf "*** line %s of %s" __LINE__ __SOURCE_FILE__)
        let lines = bitcoinCli.ReadToEnd()
        Console.WriteLine(sprintf "*** line %s of %s" __LINE__ __SOURCE_FILE__)
        let output = String.concat "\n" lines
        let txIdList = JsonConvert.DeserializeObject<list<string>> output
        Console.WriteLine(sprintf "*** line %s of %s" __LINE__ __SOURCE_FILE__)
        List.map (fun (txIdString: string) -> TxId <| uint256 txIdString) txIdList

    member this.RpcAddr(): string =
        "127.0.0.1:18554"

    member this.RpcUrl(): string =
        SPrintF3 "http://%s:%s@%s" this.RpcUser this.RpcPassword (this.RpcAddr())

