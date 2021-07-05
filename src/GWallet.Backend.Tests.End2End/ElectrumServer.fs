namespace GWallet.Backend.Tests.End2End

open System
open System.IO
open System.Linq

open NBitcoin
open DotNetLightning.Utils

open GWallet.Backend
open GWallet.Backend.UtxoCoin
open GWallet.Backend.FSharpUtil.UwpHacks

type ElectrumServer =
    {
        DbDir: string
        XProcess: XProcess
    }

    interface IDisposable with
        member self.Dispose() =
            XProcess.WaitForExit true self.XProcess
            Directory.Delete(self.DbDir, true)

    static member Start(bitcoind: Bitcoind): Async<ElectrumServer> = async {

        // create electrs database directory
        let dbDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
        Directory.CreateDirectory dbDir |> ignore

        // start electrs process
        let xprocess =
            let dbDirMnt = // TODO: extract out this function.
                if dbDir.Contains ":" then
                    let dbDirHead = dbDir.[0].ToString().ToLower()
                    let dbDirTail = dbDir.Substring 1
                    "/mnt/" + dbDirHead + dbDirTail.Replace("\\", "/").Replace(":", "")
                else dbDir
            let args =
                (SPrintF4
                    "\
                    --db-dir %s \
                    --daemon-dir %s \
                    --network regtest \
                    --electrum-rpc-addr %s \
                    --daemon-rpc-addr %s \
                    "
                    dbDirMnt
                    bitcoind.DataDirMnt
                    Config.ElectrumRpcAddress
                    Config.BitcoindRpcAddress
                )
            XProcess.Start "electrs" args Map.empty

        // skip to init message
        XProcess.WaitForMessage (fun msg -> msg.Contains "Electrum Rust Server") xprocess

        // sleep through electrs warm-up period
        do! Async.Sleep 5000

        // make ElectrumServer
        return {
            DbDir = dbDir
            XProcess = xprocess
        }
    }

    static member EstimateFeeRate(): Async<FeeRatePerKw> = async {
        let! btcPerKB =
            let averageFee (feesFromDifferentServers: list<decimal>): decimal =
                feesFromDifferentServers.Sum() / decimal (List.length feesFromDifferentServers)
            let estimateFeeJob = ElectrumClient.EstimateFee 6
            Server.Query
                Currency.BTC
                (QuerySettings.FeeEstimation averageFee)
                estimateFeeJob
                None
        let satPerKB = (Money (btcPerKB, MoneyUnit.BTC)).ToUnit MoneyUnit.Satoshi
        // 4 weight units per byte. See segwit specs.
        let kwPerKB = 4m
        let satPerKw = satPerKB / kwPerKB
        let feeRatePerKw = FeeRatePerKw (uint32 satPerKw)
        return feeRatePerKw
    }

    static member SetEstimatedFeeRate(feeRatePerKw: FeeRatePerKw) =
        let satPerKw = decimal feeRatePerKw.Value
        let kwPerKB = 4m
        let satPerKB = satPerKw * kwPerKB
        let btcPerKB = (Money (satPerKB, MoneyUnit.Satoshi)).ToUnit MoneyUnit.BTC
        ElectrumClient.RegTestFakeFeeRate <- btcPerKB

