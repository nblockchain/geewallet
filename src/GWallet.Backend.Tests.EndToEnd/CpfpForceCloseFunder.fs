namespace GWallet.Backend.Tests.EndToEnd

open System
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
open GWallet.Backend.Tests.EndToEnd.Util

[<TestFixture>]
type CpfpForceCloseFunder() =
    [<SetUp>]
    member __.SetUp () =
        Config.SetRunModeRegTest()

    [<Category("CpfpForceCloseFunder")>]
    [<Test>]
    member __.``cpfp is used when force-closing channel (funder)``() = Async.RunSynchronously <| async {
        use! walletInstance = WalletInstance.New None None
        use! bitcoind = Bitcoind.Start()
        use! _electrumServer = ElectrumServer.Start bitcoind
        use! lnd = Lnd.Start bitcoind

        do! walletInstance.FundByMining bitcoind lnd

        let! oldFeeRate = ElectrumServer.EstimateFeeRate()
        let! channelId = walletInstance.OpenChannelWithFundee bitcoind
        let newFeeRate = oldFeeRate * 5u
        ElectrumServer.SetEstimatedFeeRate newFeeRate

        let commitmentTxString = walletInstance.ChannelStore.GetCommitmentTx channelId
        let! _commitmentTxIdString = Account.BroadcastRawTransaction Currency.BTC commitmentTxString

        let commitmentTx = Transaction.Parse(commitmentTxString, Network.RegTest)
        let! commitmentTxFee = getFee commitmentTx
        let commitmentTxFeeRate =
            FeeRatePerKw.FromFeeAndVSize(commitmentTxFee, uint64 (commitmentTx.GetVirtualSize()))
        assert feeRatesApproxEqual commitmentTxFeeRate oldFeeRate

        let! recoveryTxStringNoCpfpOpt =
            Lightning.Network.ForceCloseUsingCommitmentTx
                walletInstance.Node
                commitmentTxString
                channelId
                false
        let recoveryTxStringNoCpfp =
            UnwrapOption
                recoveryTxStringNoCpfpOpt
                "force close failed to recover funds from the commitment tx"
        let recoveryTxNoCpfp = Transaction.Parse(recoveryTxStringNoCpfp, Network.RegTest)
        let! recoveryTxFeeNoCpfp = getFee recoveryTxNoCpfp
        let recoveryTxFeeRateNoCpfp =
            FeeRatePerKw.FromFeeAndVSize(recoveryTxFeeNoCpfp, uint64 (recoveryTxNoCpfp.GetVirtualSize()))
        assert feeRatesApproxEqual recoveryTxFeeRateNoCpfp newFeeRate
        let combinedFeeRateNoCpfp =
            FeeRatePerKw.FromFeeAndVSize(
                recoveryTxFeeNoCpfp + commitmentTxFee,
                uint64 (recoveryTxNoCpfp.GetVirtualSize() + commitmentTx.GetVirtualSize())
            )
        assert (not <| feeRatesApproxEqual combinedFeeRateNoCpfp oldFeeRate)
        assert (not <| feeRatesApproxEqual combinedFeeRateNoCpfp newFeeRate)
        
        let! recoveryTxStringWithCpfpOpt =
            Lightning.Network.ForceCloseUsingCommitmentTx
                walletInstance.Node
                commitmentTxString
                channelId
                true
        let recoveryTxStringWithCpfp =
            UnwrapOption
                recoveryTxStringWithCpfpOpt
                "force close failed to recover funds from the commitment tx"
        let recoveryTxWithCpfp = Transaction.Parse(recoveryTxStringWithCpfp, Network.RegTest)
        let! recoveryTxFeeWithCpfp = getFee recoveryTxWithCpfp
        let recoveryTxFeeRateWithCpfp =
            FeeRatePerKw.FromFeeAndVSize(recoveryTxFeeWithCpfp, uint64 (recoveryTxWithCpfp.GetVirtualSize()))
        assert (not <| feeRatesApproxEqual recoveryTxFeeRateWithCpfp oldFeeRate)
        assert (not <| feeRatesApproxEqual recoveryTxFeeRateWithCpfp newFeeRate)
        let combinedFeeRateWithCpfp =
            FeeRatePerKw.FromFeeAndVSize(
                recoveryTxFeeWithCpfp + commitmentTxFee,
                uint64 (recoveryTxWithCpfp.GetVirtualSize() + commitmentTx.GetVirtualSize())
            )
        assert feeRatesApproxEqual combinedFeeRateWithCpfp newFeeRate

        // Give the fundee time to see the force-close tx
        do! Async.Sleep 5000
    }
