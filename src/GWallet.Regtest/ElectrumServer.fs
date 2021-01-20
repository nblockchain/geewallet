namespace GWallet.Regtest

open System
open System.IO // For File.WriteAllText
open GWallet.Backend.FSharpUtil.UwpHacks

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
                "electrs"
                (SPrintF3
                    "\
                    --db-dir %s \
                    --daemon-dir %s \
                    --network regtest \
                    --electrum-rpc-addr [::1]:50001 \
                    --daemon-rpc-addr %s \
                    "
                    dbDir
                    bitcoind.DataDir
                    (bitcoind.RpcAddr())
                )
                Map.empty
                false
        processWrapper.WaitForMessage (fun msg -> msg.Contains "Electrum Rust Server")
        {
            DbDir = dbDir
            ProcessWrapper = processWrapper
        }

