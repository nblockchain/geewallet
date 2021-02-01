namespace GWallet.Backend.Tests.EndToEnd

open System.Threading // For AutoResetEvent and CancellationToken

open NUnit.Framework
open NBitcoin // For ExtKey
open DotNetLightning.Utils
open ResultUtils.Portability

open GWallet.Backend
open GWallet.Backend.UtxoCoin // For NormalUtxoAccount
open GWallet.Backend.UtxoCoin.Lightning
open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks
open GWallet.Regtest


[<TestFixture>]
type GeewalletToGeewalletFunder() =

    [<SetUp>]
    member __.SetUp () =
        do Config.SetRunModeRegTest()

    [<Category("GeewalletToGeewalletFunder")>]
    [<Test>]
    member __.``can send/receive monohop payments and close channel (funder)``() = Async.RunSynchronously <| async {
        use! walletInstance = WalletInstance.New None None
        use! bitcoind = Bitcoind.Start()
        use! _electrumServer = ElectrumServer.Start bitcoind
        use! lnd = Lnd.Start bitcoind
        do! walletInstance.FundByMining bitcoind lnd

        let! channelId = walletInstance.OpenChannelWithFundee bitcoind

        let! closeChannelRes = Lightning.Network.CloseChannel walletInstance.Node channelId
        match closeChannelRes with
        | Ok () -> ()
        | Error err -> failwith (SPrintF1 "error when closing channel: %s" err.Message)

        match (walletInstance.ChannelStore.ChannelInfo channelId).Status with
        | ChannelStatus.Closing -> ()
        | status -> failwith (SPrintF1 "unexpected channel status. Expected Closing, got %A" status)

        // Mine 7 blocks to make sure closing tx is confirmed
        bitcoind.GenerateBlocksToBurnAddress Config.MinimumDepth
    
        let rec waitForClosingTxConfirmed attempt = async {
            Infrastructure.LogDebug (SPrintF1 "Checking if closing tx is finished, attempt #%d" attempt)
            if attempt = 10 then
                return Error "Closing tx not confirmed after maximum attempts"
            else
                let! txIsConfirmed = Lightning.Network.CheckClosingFinished (walletInstance.ChannelStore.ChannelInfo channelId)
                if txIsConfirmed then
                    return Ok ()
                else
                    do! Async.Sleep 1000
                    return! waitForClosingTxConfirmed (attempt + 1)
                
        }

        let! closingTxConfirmedRes = waitForClosingTxConfirmed 0
        match closingTxConfirmedRes with
        | Ok () -> ()
        | Error err -> failwith (SPrintF1 "error when waiting for closing tx to confirm: %s" err)

        return ()
    }
