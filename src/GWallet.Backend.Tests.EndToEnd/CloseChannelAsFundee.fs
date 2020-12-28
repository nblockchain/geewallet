namespace GWallet.Backend.Tests.EndToEnd

open System.Threading // For AutoResetEvent and CancellationToken

open NUnit.Framework
open BTCPayServer.Lightning
open DotNetLightning.Utils
open ResultUtils.Portability

open GWallet.Backend
open GWallet.Backend.UtxoCoin // For NormalUtxoAccount
open GWallet.Backend.UtxoCoin.Lightning
open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks
open GWallet.Regtest


[<TestFixture>]
type CloseChannelAsFundee() =
    
    [<SetUp>]
    member __.SetUp () =
        do Config.SetRunModeRegTest()

    [<Test>]
    member __.``can close channel from LND (as fundee)``() = Async.RunSynchronously <| async {
        use! walletInstance = WalletInstance.New None None
        use bitcoind = Bitcoind.Start()
        use _electrumServer = ElectrumServer.Start bitcoind
        use! lnd = Lnd.Start bitcoind

        let! channelId, fundingOutPoint = async {
            try 
                let! channelId, fundingOutPoint = GwalletToLndChannelManagement.AcceptChannel walletInstance bitcoind lnd
                return channelId, fundingOutPoint
            with
            | _ex ->
                Assert.Inconclusive "test cannot be run because channel opening failed"
                return failwith "unreachable"
        }

        let closeChannelTask = async {
            let! connectionResult = lnd.ConnectTo walletInstance.NodeEndPoint
            match connectionResult with
            | ConnectionResult.CouldNotConnect ->
                failwith "lnd could not connect back to us"
            | _connectionResult -> ()
            do! Async.Sleep 1000
            do! lnd.CloseChannel fundingOutPoint
            return ()
        }

        let awaitCloseTask = async {
            let rec receiveEvent () = async {
                let! receivedEvent = Lightning.Network.ReceiveLightningEvent walletInstance.Node channelId
                match receivedEvent with
                | Error err ->
                    return Error (SPrintF1 "Failed to receive shutdown msg from LND: %A" err)
                | Ok event when event = IncomingChannelEvent.Shutdown ->
                    return Ok ()
                | _event -> return! receiveEvent ()
            }

            let! receiveEventRes = receiveEvent()
            UnwrapResult receiveEventRes "failed to accept close channel"

            // Wait for the closing transaction to appear in mempool
            while bitcoind.GetTxIdsInMempool().Length = 0 do
                do! Async.Sleep 500

            // Mine blocks on top of the closing transaction to make it confirmed.
            bitcoind.GenerateBlocks Config.MinimumDepth walletInstance.Address
            return ()
        }
        let! (), () = AsyncExtensions.MixedParallel2 closeChannelTask awaitCloseTask
        return ()
    }
    
