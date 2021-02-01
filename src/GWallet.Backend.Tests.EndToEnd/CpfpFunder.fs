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
type CpfpFunder() =
    [<SetUp>]
    member __.SetUp () =
        Config.SetRunModeRegTest()

    [<Category("GeewalletForceCloseFunder")>]
    [<Test>]
    member __.``cpfp is used when force-closing channel (funder)``() = Async.RunSynchronously <| async {
        use! walletInstance = WalletInstance.New None None
        use! bitcoind = Bitcoind.Start()
        use! _electrumServer = ElectrumServer.Start bitcoind
        use! lnd = Lnd.Start bitcoind

        do! walletInstance.FundByMining bitcoind lnd

        let! channelId = walletInstance.OpenChannelWithFundee bitcoind

        (*
        plan:

        open a channel.
        raise the electrum fake fee rate
        force-close the channel.
        check that the fee of the force-close tx is insufficient.
        generate a recovery tx with cpfp=false
            check that the recovery tx has a fee to pay for just itself
        generate a recovery tx with cpfp=true
            check that the recovery tx has a fee to pay for both
        *)
    }
