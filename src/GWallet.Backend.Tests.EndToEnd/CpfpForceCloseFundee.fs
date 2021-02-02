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
open GWallet.Backend.Tests.EndToEnd.Util
open GWallet.Regtest

[<TestFixture>]
type CpfpForceCloseFundee() =
    
    [<SetUp>]
    member __.SetUp () =
        Config.SetRunModeRegTest()
    
    [<Category("CpfpForceCloseFundee")>]
    [<Test>]
    member __.``cpfp is used when force-closing channel (fundee)``() = Async.RunSynchronously <| async {
        use! walletInstance = WalletInstance.New (Some Config.FundeeLightningIPEndpoint) (Some Config.FundeeAccountsPrivateKey)

        let! oldFeeRate = ElectrumServer.EstimateFeeRate()
        let! channelId = walletInstance.AcceptChannelFromFunder()
        let newFeeRate = oldFeeRate * 5u
        ElectrumServer.SetEstimatedFeeRate newFeeRate

        let rec waitForForceClose(): Async<string> = async {
            let! closingTxInfoOpt = walletInstance.ChannelStore.CheckForClosingTx channelId
            match closingTxInfoOpt with
            | None ->
                do! Async.Sleep 500
                return! waitForForceClose()
            | Some (forceCloseTxIdString, _blockHeightOpt) ->
                return forceCloseTxIdString
        }
        let! forceCloseTxIdString = waitForForceClose()
        let! forceCloseTxString =
            Server.Query
                Currency.BTC
                (QuerySettings.Default ServerSelectionMode.Fast)
                (ElectrumClient.GetBlockchainTransaction forceCloseTxIdString)
                None

        let forceCloseTx = Transaction.Parse(forceCloseTxString, Network.RegTest)
        let! forceCloseTxFee = getFee forceCloseTx
        let forceCloseTxFeeRate =
            FeeRatePerKw.FromFeeAndVSize(forceCloseTxFee, uint64 (forceCloseTx.GetVirtualSize()))
        assert feeRatesApproxEqual forceCloseTxFeeRate oldFeeRate
        
        let! recoveryTxStringNoCpfpOpt =
            Lightning.Network.CreateRecoveryTxForRemoteForceClose
                walletInstance.Node
                channelId
                (forceCloseTx.GetHash().ToString())
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
                recoveryTxFeeNoCpfp + forceCloseTxFee,
                uint64 (recoveryTxNoCpfp.GetVirtualSize() + forceCloseTx.GetVirtualSize())
            )
        assert (not <| feeRatesApproxEqual combinedFeeRateNoCpfp oldFeeRate)
        assert (not <| feeRatesApproxEqual combinedFeeRateNoCpfp newFeeRate)
        
        let! recoveryTxStringWithCpfpOpt =
            Lightning.Network.CreateRecoveryTxForRemoteForceClose
                walletInstance.Node
                channelId
                (forceCloseTx.GetHash().ToString())
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
                recoveryTxFeeWithCpfp + forceCloseTxFee,
                uint64 (recoveryTxWithCpfp.GetVirtualSize() + forceCloseTx.GetVirtualSize())
            )
        assert feeRatesApproxEqual combinedFeeRateWithCpfp newFeeRate
    }
