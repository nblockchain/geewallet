namespace GWallet.Backend.Tests.End2End

open System
open System.IO

open Newtonsoft.Json
open NBitcoin
open DotNetLightning.Utils

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks

type Bitcoind =
    {
        DataDir: string
        DataDirMnt: string
        RpcUser: string
        RpcPassword: string
        XProcess: XProcess
    }

    interface IDisposable with
        member self.Dispose() =
            XProcess.WaitForExit true self.XProcess
            Directory.Delete(self.DataDir, true)

    static member Start(): Async<Bitcoind> = async {

        // create bitcoin config file
        let dataDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
        Directory.CreateDirectory dataDir |> ignore
        let rpcUser = Path.GetRandomFileName()
        let rpcPassword = Path.GetRandomFileName()
        let confPath = Path.Combine(dataDir, "bitcoin.conf")
        let confText =
            String.Join(
                "\n", // NOTE: this file will be read by a linux program, so \r isn't needed.
                [
                     ""
                     "txindex=1"
                     "printtoconsole=1"
                     ("rpcallowip=" + Config.BitcoindRpcAllowIP)
                     ("zmqpubrawblock=" + "tcp://" + Config.BitcoindZeromqPublishRawBlockAddress)
                     ("zmqpubrawtx=" + "tcp://" + Config.BitcoindZeromqPublishRawTxAddress)
                     ("fallbackfee=" + string UtxoCoin.ElectrumClient.RegTestFakeFeeRate)
                     "[regtest]"
                     ("rpcbind=" + Config.BitcoindRpcIP)
                     ("rpcport=" + Config.BitcoindRpcPort)
                ]
            )
        File.WriteAllText(confPath, confText)

        // start bitcoind process
        let dataDirMnt = // TODO: extract out this function.
            if dataDir.Contains ":" then
                let dataDirHead = dataDir.[0].ToString().ToLower()
                let dataDirTail = dataDir.Substring 1
                "/mnt/" + dataDirHead + dataDirTail.Replace("\\", "/").Replace(":", "")
            else dataDir
        let args = SPrintF1 "-regtest -datadir=%s" dataDirMnt
        let xprocess = XProcess.Start "bitcoind" args Map.empty

        // skip to init message
        XProcess.WaitForMessage (fun msg -> msg.EndsWith "init message: Done loading") xprocess

        // sleep through bitcoind warm-up period
        do! Async.Sleep 2000

        // make Bitcoind
        return {
            DataDir = dataDir
            DataDirMnt = dataDirMnt
            RpcUser = rpcUser
            RpcPassword = rpcPassword
            XProcess = xprocess
        }
    }

    member self.GenerateBlocks (number: BlockHeightOffset32) (address: BitcoinAddress) =
        let bitcoinCli =
            XProcess.Start
                "bitcoin-cli"
                (SPrintF3 "-regtest -datadir=%s generatetoaddress %i %s" self.DataDirMnt number.Value (string address))
                Map.empty
        XProcess.WaitForExit false bitcoinCli

    member this.GenerateBlocksToDummyAddress (number: BlockHeightOffset32) =
        let address =
            let key = new Key()
            key.PubKey.GetScriptAddress(Network.RegTest)
        this.GenerateBlocks number address

    member self.GetTxIdsInMempool(): list<TxId> =
        let bitcoinCli =
            XProcess.Start
                "bitcoin-cli"
                (SPrintF1 "-regtest -datadir=%s getrawmempool" self.DataDirMnt)
                Map.empty
        let lines = XProcess.ReadMessages bitcoinCli
        let output = String.concat "\n" lines
        let txIdList = JsonConvert.DeserializeObject<list<string>> output
        List.map (fun (txIdString: string) -> TxId <| uint256 txIdString) txIdList

