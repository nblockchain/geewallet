namespace GWallet.Regtest

open System
open System.IO // For File.WriteAllText

type ElectrumServer = {
    DbDir: string
    ProcessWrapper: ProcessWrapper
} with
    interface IDisposable with
        member this.Dispose() =
            this.ProcessWrapper.Process.Kill()
            this.ProcessWrapper.WaitForExit()
            Directory.Delete(this.DbDir, true)

    static member Start(bitcoind: Bitcoind): ElectrumServer =
        let dbDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
        Directory.CreateDirectory dbDir |> ignore
        let processWrapper =
            ProcessWrapper.New
                "electrumx_server"
                ""
                (Map.ofList <| [
                    "SERVICES", "tcp://[::1]:50001";
                    "COIN", "BitcoinSegwit";
                    "NET", "regtest";
                    "DAEMON_URL", bitcoind.RpcUrl;
                    "DB_DIRECTORY", dbDir
                ])
                true
        processWrapper.WaitForMessage (fun msg -> msg.EndsWith "TCP server listening on [::1]:50001")
        {
            DbDir = dbDir
            ProcessWrapper = processWrapper
        }

