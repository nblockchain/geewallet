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
type GeewalletForceCloseFunder() =
    [<SetUp>]
    member __.SetUp () =
        Config.SetRunModeRegTest()

    [<Category("GeewalletForceCloseFunder")>]
    [<Test>]
    member __.``can send/receive monohop payments and force-close channel (funder)``() = Async.RunSynchronously <| async {
        use! walletInstance = WalletInstance.New None None
        use! bitcoind = Bitcoind.Start()
        use! _electrumServer = ElectrumServer.Start bitcoind
        use! lnd = Lnd.Start bitcoind

        do! walletInstance.FundByMining bitcoind lnd

        let! channelId = walletInstance.OpenChannelWithFundee bitcoind

        let! _forceCloseTxId = Lightning.Network.ForceClose walletInstance.Node channelId

        let locallyForceClosedData =
            match (walletInstance.ChannelStore.ChannelInfo channelId).Status with
            | ChannelStatus.LocallyForceClosed locallyForceClosedData ->
                locallyForceClosedData
            | status -> failwith (SPrintF1 "unexpected channel status. Expected LocallyForceClosed, got %A" status)

        // wait for force-close transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500
        
        Infrastructure.LogDebug (SPrintF1 "the time lock is %i blocks" locallyForceClosedData.ToSelfDelay)

        let! balanceBeforeFundsReclaimed = walletInstance.GetBalance()

        // Mine the force-close tx into a block
        bitcoind.GenerateBlocksToBurnAddress (BlockHeightOffset32 1u)

        // wait for fundee's recovery tx to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500

        // Mine the fundee's recovery tx
        bitcoind.GenerateBlocksToBurnAddress (BlockHeightOffset32 1u)

        // Mine blocks to release time-lock
        bitcoind.GenerateBlocksToBurnAddress
            (BlockHeightOffset32 (uint32 locallyForceClosedData.ToSelfDelay))
        
        let! _recoveryTxId =
            UtxoCoin.Account.BroadcastRawTransaction
                locallyForceClosedData.Currency
                locallyForceClosedData.SpendingTransactionString

        // wait for recovery transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500
        
        // Mine the recovery tx into a block
        bitcoind.GenerateBlocksToBurnAddress (BlockHeightOffset32 1u)

        Infrastructure.LogDebug ("waiting for our wallet balance to increase")
        let! _balanceAfterFundsReclaimed =
            let amount = balanceBeforeFundsReclaimed + Money(1.0m, MoneyUnit.Satoshi)
            walletInstance.WaitForBalance amount

        // Give the fundee time to see their funds recovered before closing bitcoind/electrum
        do! Async.Sleep 3000

        return ()
    }

