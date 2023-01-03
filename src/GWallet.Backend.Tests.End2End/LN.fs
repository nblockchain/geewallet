// because of the use of internal AcceptCloseChannel
#nowarn "44"

namespace GWallet.Backend.Tests.End2End

open System
open System.IO
open System.Threading

open NBitcoin
open NUnit.Framework
open DotNetLightning.Payment
open DotNetLightning.Utils
open ResultUtils.Portability

open GWallet.Backend
open GWallet.Backend.UtxoCoin
open GWallet.Backend.UtxoCoin.Lightning
open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks


module Helpers =
    let RunAsyncTest (testJob: Async<unit>) =
        let testName = (new System.Diagnostics.StackTrace()).GetFrame(1).GetMethod().Name
        Console.WriteLine("*** Starting test: " + testName)
        try
            Async.RunSynchronously testJob
        with
        | exn ->
            Console.WriteLine("*** CAUGHT EXCEPTION: " + exn.ToString())
            reraise()
        Console.WriteLine("*** Completed test: " + testName)


[<TestFixture>]
type LN() =
    do Config.SetRunModeToTesting()

    let walletToWalletTestPayment1Amount = Money (0.01m, MoneyUnit.BTC)
    let walletToWalletTestPayment2Amount = Money (0.015m, MoneyUnit.BTC)

    let TearDown walletInstance bitcoind electrumServer lnd =
        (walletInstance :> IDisposable).Dispose()
        (lnd :> IDisposable).Dispose()
        (electrumServer :> IDisposable).Dispose()
        (bitcoind :> IDisposable).Dispose()
        Console.WriteLine("*** TearDown completed successfully")

    let OpenChannelWithFundee (nodeOpt: Option<NodeEndPoint>) =
        async {
            let! clientWallet = ClientWalletInstance.New None
            let! bitcoind = Bitcoind.Start()
            let! electrumServer = ElectrumServer.Start bitcoind
            let! lnd = Lnd.Start bitcoind

            do! clientWallet.FundByMining bitcoind lnd

            let! lndEndPoint = lnd.GetEndPoint()

            let nodeEndPoint =
                match nodeOpt with
                | None -> lndEndPoint
                | Some node -> node

            let! channelId, fundingAmount = clientWallet.OpenChannelWithFundee bitcoind nodeEndPoint

            let channelInfoAfterOpening = clientWallet.ChannelStore.ChannelInfo channelId
            match channelInfoAfterOpening.Status with
            | ChannelStatus.Active -> ()
            | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

            return channelId, clientWallet, bitcoind, electrumServer, lnd, fundingAmount
        }

    let AcceptChannelFromLndFunder () =
        async {
            let! serverWallet = ServerWalletInstance.New Config.FundeeLightningIPEndpoint None
            Console.WriteLine("*** line " + __LINE__)
            let! bitcoind = Bitcoind.Start()
            Console.WriteLine("*** line " + __LINE__)
            let! electrumServer = ElectrumServer.Start bitcoind
            Console.WriteLine("*** line " + __LINE__)
            let! lnd = Lnd.Start bitcoind
            Console.WriteLine("*** line " + __LINE__)

            do! lnd.FundByMining bitcoind
            Console.WriteLine("*** line " + __LINE__)

            let! feeRate = ElectrumServer.EstimateFeeRate()
            Console.WriteLine("*** line " + __LINE__)
            let fundingAmount = Money(0.1m, MoneyUnit.BTC)
            Console.WriteLine("*** line " + __LINE__)
            let acceptChannelTask = Lightning.Network.AcceptChannel serverWallet.NodeServer
            Console.WriteLine("*** line " + __LINE__)
            let openChannelTask = async {
                match serverWallet.NodeEndPoint with
                | EndPointType.Tcp endPoint ->
                    do! lnd.ConnectTo endPoint
                    Console.WriteLine("*** line " + __LINE__)
                    return!
                        lnd.OpenChannel
                            endPoint
                            fundingAmount
                            feeRate
                | EndPointType.Tor _torEndPoint ->
                    return failwith "unreachable because tests use TCP"
            }

            let! acceptChannelRes, openChannelRes = AsyncExtensions.MixedParallel2 acceptChannelTask openChannelTask
            Console.WriteLine("*** line " + __LINE__)
            let (channelId, _) = UnwrapResult acceptChannelRes "AcceptChannel failed"
            Console.WriteLine("*** line " + __LINE__)
            UnwrapResult openChannelRes "lnd.OpenChannel failed"
            Console.WriteLine("*** line " + __LINE__)

            // Wait for the funding transaction to appear in mempool
            while bitcoind.GetTxIdsInMempool().Length = 0 do
                Thread.Sleep 500
            Console.WriteLine("*** line " + __LINE__)

            // Mine blocks on top of the funding transaction to make it confirmed.
            let minimumDepth = BlockHeightOffset32 6u
            bitcoind.GenerateBlocksToDummyAddress minimumDepth
            Console.WriteLine("*** line " + __LINE__)

            do! serverWallet.WaitForFundingConfirmed channelId
            Console.WriteLine("*** line " + __LINE__)

            let initialInterval = TimeSpan.FromSeconds 1.0

            let rec tryAcceptLock (backoff: TimeSpan) =
                async {
                    let! lockFundingRes = Lightning.Network.AcceptLockChannelFunding serverWallet.NodeServer channelId
                    Console.WriteLine("*** line " + __LINE__)
                    match lockFundingRes with
                    | Error error ->
                            let backoffMillis = (int backoff.TotalMilliseconds)
                            Infrastructure.LogDebug <| SPrintF1 "accept error: %s" error.Message
                            Infrastructure.LogDebug <| SPrintF1 "retrying in %ims" backoffMillis
                            do! Async.Sleep backoffMillis
                            return! tryAcceptLock (backoff + backoff)
                    | Ok _ ->
                        return ()
                }

            do! tryAcceptLock initialInterval
            Console.WriteLine("*** line " + __LINE__)

            let channelInfo = serverWallet.ChannelStore.ChannelInfo channelId
            Console.WriteLine("*** line " + __LINE__)
            match channelInfo.Status with
            | ChannelStatus.Active -> ()
            | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)
            Console.WriteLine("*** line " + __LINE__)

            return channelId, serverWallet, bitcoind, electrumServer, lnd
        }

    let AcceptChannelFromGeewalletFunder () =
        async {
            let! serverWallet = ServerWalletInstance.New Config.FundeeLightningIPEndpoint (Some Config.FundeeAccountsPrivateKey)
            let! pendingChannelRes =
                Lightning.Network.AcceptChannel
                    serverWallet.NodeServer

            let (channelId, _) = UnwrapResult pendingChannelRes "OpenChannel failed"

            let! lockFundingRes = Lightning.Network.AcceptLockChannelFunding serverWallet.NodeServer channelId
            UnwrapResult lockFundingRes "LockChannelFunding failed"

            let channelInfo = serverWallet.ChannelStore.ChannelInfo channelId
            match channelInfo.Status with
            | ChannelStatus.Active -> ()
            | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

            if Money(channelInfo.Balance, MoneyUnit.BTC) <> Money(0.0m, MoneyUnit.BTC) then
                return failwith "incorrect balance after accepting channel"

            return serverWallet, channelId
        }

    let ClientCloseChannel (clientWallet: ClientWalletInstance) (bitcoind: Bitcoind) channelId =
        async {
            let! closeChannelRes = Lightning.Network.CloseChannel clientWallet.NodeClient channelId
            match closeChannelRes with
            | Ok _ -> ()
            | Error err -> return failwith (SPrintF1 "error when closing channel: %s" (err :> IErrorMsg).Message)

            match (clientWallet.ChannelStore.ChannelInfo channelId).Status with
            | ChannelStatus.Closing -> ()
            | status -> return failwith (SPrintF1 "unexpected channel status. Expected Closing, got %A" status)

            // Give fundee time to see the closing tx on blockchain
            do! Async.Sleep 10000

            // Mine 10 blocks to make sure closing tx is confirmed
            bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 (uint32 10))

            let rec waitForClosingTxConfirmed attempt = async {
                Infrastructure.LogDebug (SPrintF1 "Checking if closing tx is finished, attempt #%d" attempt)
                if attempt = 10 then
                    return Error "Closing tx not confirmed after maximum attempts"
                else
                    let! closingTxResult = Lightning.Network.CheckClosingFinished clientWallet.ChannelStore channelId
                    match closingTxResult with
                    | Tx (Full, _closingTx) ->
                        return Ok ()
                    | _ ->
                        do! Async.Sleep 1000
                        return! waitForClosingTxConfirmed (attempt + 1)
            }

            let! closingTxConfirmedRes = waitForClosingTxConfirmed 0
            match closingTxConfirmedRes with
            | Ok _ -> ()
            | Error err -> return failwith (SPrintF1 "error when waiting for closing tx to confirm: %s" err)
        }

    let SendHtlcPaymentsToLnd (clientWallet: ClientWalletInstance)
                              (lnd: Lnd)
                              (channelId: ChannelIdentifier)
                              (fundingAmount: Money) =
        async {
            let channelInfoBeforeAnyPayment = clientWallet.ChannelStore.ChannelInfo channelId
            match channelInfoBeforeAnyPayment.Status with
            | ChannelStatus.Active -> ()
            | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

            let! sendHtlcPayment1Res =
                async {
                    let transferAmount =
                        let accountBalance = Money(channelInfoBeforeAnyPayment.SpendableBalance, MoneyUnit.BTC)
                        TransferAmount (walletToWalletTestPayment1Amount.ToDecimal MoneyUnit.BTC, accountBalance.ToDecimal MoneyUnit.BTC, Currency.BTC)
                    let! invoiceOpt = 
                        lnd.CreateInvoice transferAmount None
                    let invoice = UnwrapOption invoiceOpt "Failed to create first invoice"

                    return!
                        Lightning.Network.SendHtlcPayment
                            clientWallet.NodeClient
                            channelId
                            (PaymentInvoice.Parse invoice.BOLT11)
                            true
                }
            UnwrapResult sendHtlcPayment1Res "SendHtlcPayment failed"

            let channelInfoAfterPayment1 = clientWallet.ChannelStore.ChannelInfo channelId
            match channelInfoAfterPayment1.Status with
            | ChannelStatus.Active -> ()
            | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

            let! lndBalanceAfterPayment1 = lnd.ChannelBalance()

            if Money(channelInfoAfterPayment1.Balance, MoneyUnit.BTC) <> fundingAmount - walletToWalletTestPayment1Amount then
                return failwith "incorrect balance after payment 1"
            if lndBalanceAfterPayment1 <> walletToWalletTestPayment1Amount then
                return failwith "incorrect lnd balance after payment 1"

            let! sendHtlcPayment2Res =
                async {
                    let transferAmount =
                        let accountBalance = Money(channelInfoBeforeAnyPayment.SpendableBalance, MoneyUnit.BTC)
                        TransferAmount (walletToWalletTestPayment2Amount.ToDecimal MoneyUnit.BTC, accountBalance.ToDecimal MoneyUnit.BTC, Currency.BTC)
                    let! invoiceOpt = 
                        lnd.CreateInvoice transferAmount None
                    let invoice = UnwrapOption invoiceOpt "Failed to create second invoice"

                    return!
                        Lightning.Network.SendHtlcPayment
                            clientWallet.NodeClient
                            channelId
                            (PaymentInvoice.Parse invoice.BOLT11)
                            true
                }
            UnwrapResult sendHtlcPayment2Res "SendHtlcPayment failed"

            let channelInfoAfterPayment2 = clientWallet.ChannelStore.ChannelInfo channelId
            match channelInfoAfterPayment2.Status with
            | ChannelStatus.Active -> ()
            | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)
            let! lndBalanceAfterPayment2 = lnd.ChannelBalance()

            if Money(channelInfoAfterPayment2.Balance, MoneyUnit.BTC) <> fundingAmount - walletToWalletTestPayment1Amount - walletToWalletTestPayment2Amount then
                return failwith "incorrect balance after payment 2"
            if lndBalanceAfterPayment2 <> lndBalanceAfterPayment1 + walletToWalletTestPayment2Amount then
                return failwith "incorrect lnd balance after payment 2"
        }

    let SendHtlcPaymentToGW (channelId) (clientWallet: ClientWalletInstance) (fileName: string) =
        async {
            let! invoiceInString =
                let rec readInvoice (path: string) =
                    async {
                        try
                            let invoiceString = File.ReadAllText path
                            if String.IsNullOrWhiteSpace invoiceString then
                                do! Async.Sleep 500
                                return! readInvoice path
                            else
                                return invoiceString
                        with
                        | :? FileNotFoundException ->
                            do! Async.Sleep 500
                            return! readInvoice path
                    }

                readInvoice (Path.Combine (Path.GetTempPath(), fileName))

            return!
                Lightning.Network.SendHtlcPayment
                    clientWallet.NodeClient
                    channelId
                    (PaymentInvoice.Parse invoiceInString)
                    true
        }

    let ReceiveHtlcPaymentToGW (channelId) (serverWallet: ServerWalletInstance) (fileName: string) =
        async {
            let invoiceManager = InvoiceManagement (serverWallet.Account :?> NormalUtxoAccount, serverWallet.Password)
            let amountInSatoshis =
                Convert.ToUInt64 walletToWalletTestPayment1Amount.Satoshi
            let invoice1InString = invoiceManager.CreateInvoice amountInSatoshis "Payment 1"

            File.WriteAllText (Path.Combine (Path.GetTempPath(), fileName), invoice1InString)

            let! receiveLightningEventRes = Lightning.Network.ReceiveLightningEvent serverWallet.NodeServer channelId true
            match receiveLightningEventRes with
            | Ok (HtlcPayment htlcStatus) ->
                Assert.AreNotEqual (HtlcSettleStatus.Fail, htlcStatus, "htlc payment failed gracefully")
                Assert.AreNotEqual (HtlcSettleStatus.NotSettled, htlcStatus, "htlc payment didn't get settled")
                return htlcStatus
            | _ ->
                return failwith "Receiving htlc failed."
        }

    [<Category "G2G_ChannelOpening_Funder">]
    [<Test>]
    member __.``can open channel with geewallet (funder)``() = Helpers.RunAsyncTest <| async {
        let! _channelId, clientWallet, bitcoind, electrumServer, lnd, _fundingAmount =
            OpenChannelWithFundee (Some Config.FundeeNodeEndpoint)

        TearDown clientWallet bitcoind electrumServer lnd
    }

    [<Category "G2G_ChannelOpening_Fundee">]
    [<Test>]
    member __.``can open channel with geewallet (fundee)``() = Helpers.RunAsyncTest <| async {
        let! serverWallet, _channelId = AcceptChannelFromGeewalletFunder ()

        (serverWallet :> IDisposable).Dispose()
    }

    [<Category "G2G_ChannelClosingAfterJustOpening_Funder">]
    [<Test>]
    member __.``can close channel with geewallet (funder)``() = Helpers.RunAsyncTest <| async {
        let! channelId, clientWallet, bitcoind, electrumServer, lnd, _fundingAmount =
            try
                OpenChannelWithFundee (Some Config.FundeeNodeEndpoint)
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: channel-closing inconclusive because Channel open failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"

        do! ClientCloseChannel clientWallet bitcoind channelId

        TearDown clientWallet bitcoind electrumServer lnd
    }

    [<Category "G2G_ChannelClosingAfterJustOpening_Fundee">]
    [<Test>]
    member __.``can close channel with geewallet (fundee)``() = Helpers.RunAsyncTest <| async {
        let! serverWallet, channelId = AcceptChannelFromGeewalletFunder ()

        let! closeChannelRes = Lightning.Network.AcceptCloseChannel serverWallet.NodeServer channelId
        match closeChannelRes with
        | Ok _ -> ()
        | Error err -> return failwith (SPrintF1 "failed to accept close channel: %A" err)

        (serverWallet :> IDisposable).Dispose()
    }

    [<Category "G2G_ChannelClosingAfterSendingHTLCPayments_Funder">]
    [<Test>]
    member __.``can close channel after sending HTLC payments (funder)``() = Helpers.RunAsyncTest <| async {
        let! channelId, clientWallet, bitcoind, electrumServer, lnd, _fundingAmount =
            try
                OpenChannelWithFundee (Some Config.FundeeNodeEndpoint)
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: channel-closing inconclusive because Channel open failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"

        try
            let! sendHtlcPaymenRes = SendHtlcPaymentToGW channelId clientWallet "invoice.txt"
            UnwrapResult sendHtlcPaymenRes "sending htlc failed."
        with
        | ex ->
            Assert.Fail (
                sprintf
                    "Inconclusive: channel-closing inconclusive because sending of htlc payments failed, fix this first: %s"
                    (ex.ToString())
            )
            failwith "unreachable"

        do! ClientCloseChannel clientWallet bitcoind channelId

        TearDown clientWallet bitcoind electrumServer lnd
    }


    [<Category "G2G_ChannelClosingAfterSendingHTLCPayments_Fundee">]
    [<Test>]
    member __.``can close channel after receiving mono-hop unidirectional payments, with geewallet (fundee)``() = Helpers.RunAsyncTest <| async {
        let! serverWallet, channelId = AcceptChannelFromGeewalletFunder ()

        do! ReceiveHtlcPaymentToGW channelId serverWallet "invoice.txt" |> Async.Ignore

        let! closeChannelRes = Lightning.Network.AcceptCloseChannel serverWallet.NodeServer channelId
        match closeChannelRes with
        | Ok _ -> ()
        | Error err -> return failwith (SPrintF1 "failed to accept close channel: %A" err)

        (serverWallet :> IDisposable).Dispose()
    }

    [<Category "G2G_ChannelRemoteForceClosingByFunder_Funder">]
    [<Test>]
    member __.``can send htlc payments and handle remote force-close of channel by funder (funder)``() = Helpers.RunAsyncTest <| async {
        let! channelId, clientWallet, bitcoind, electrumServer, lnd, _fundingAmount =
            try
                OpenChannelWithFundee (Some Config.FundeeNodeEndpoint)
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: channel remote-force-closing inconclusive because Channel open failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"

        try
            let! sendPaymentRes = SendHtlcPaymentToGW channelId clientWallet "invoice.txt"
            UnwrapResult sendPaymentRes "sending htlc failed."
        with
        | ex ->
            Assert.Fail (
                sprintf
                    "Inconclusive: channel remote-force-closing inconclusive because sending of htlc payments failed, fix this first: %s"
                    (ex.ToString())
            )
            failwith "unreachable"

        let! _forceCloseTxId = (Lightning.Node.Client clientWallet.NodeClient).ForceCloseChannel channelId

        let locallyForceClosedData =
            match (clientWallet.ChannelStore.ChannelInfo channelId).Status with
            | ChannelStatus.LocallyForceClosed locallyForceClosedData ->
                locallyForceClosedData
            | status -> failwith (SPrintF1 "unexpected channel status. Expected LocallyForceClosed, got %A" status)

        // wait for force-close transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500

        Infrastructure.LogDebug (SPrintF1 "the time lock is %i blocks" locallyForceClosedData.ToSelfDelay)

        let! balanceBeforeFundsReclaimed = clientWallet.GetBalance()

        // Mine the force-close tx into a block
        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

        // wait for fundee's recovery tx to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500

        // Mine the fundee's recovery tx
        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

        // Mine blocks to release time-lock
        bitcoind.GenerateBlocksToDummyAddress
            (BlockHeightOffset32 (uint32 locallyForceClosedData.ToSelfDelay))

        let! spendingTxResult =
            let commitmentTx = clientWallet.ChannelStore.GetCommitmentTx channelId
            (Lightning.Node.Client clientWallet.NodeClient).CreateRecoveryTxForForceClose
                channelId
                commitmentTx

        let recoveryTx = UnwrapResult spendingTxResult "Local output is dust, recovery tx cannot be created"

        let! _recoveryTxId =
            ChannelManager.BroadcastRecoveryTxAndCloseChannel recoveryTx clientWallet.ChannelStore

        // wait for spending transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500

        // Mine the spending tx into a block
        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

        Infrastructure.LogDebug "waiting for our wallet balance to increase"
        let! _balanceAfterFundsReclaimed =
            let amount = balanceBeforeFundsReclaimed + Money(1.0m, MoneyUnit.Satoshi)
            clientWallet.WaitForBalance amount

        // Give the fundee time to see their funds recovered before closing bitcoind/electrum
        do! Async.Sleep 3000

        TearDown clientWallet bitcoind electrumServer lnd
    }

    [<Category "G2G_ChannelRemoteForceClosingByFunder_Fundee">]
    [<Test>]
    member __.``can receive htlc payments and handle remote force-close of channel by funder (fundee)``() = Helpers.RunAsyncTest <| async {
        let! serverWallet, channelId =
            try
                AcceptChannelFromGeewalletFunder ()
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: channel remote-force-closing inconclusive because Channel accept failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"

        try
            do! ReceiveHtlcPaymentToGW channelId serverWallet "invoice.txt" |> Async.Ignore
        with
        | ex ->
            Assert.Fail (
                sprintf
                    "Inconclusive: channel remote-force-closing inconclusive because receiving of htlc payments failed, fix this first: %s"
                    (ex.ToString())
            )
            failwith "unreachable"

        let! balanceBeforeFundsReclaimed = serverWallet.GetBalance()

        let rec waitForRemoteForceClose() = async {
            let! closingInfoOpt = serverWallet.ChannelStore.CheckForClosingTx channelId
            match closingInfoOpt with
            | Some (ClosingTx.ForceClose closingTx, Some _closingTxConfirmations) ->
                return!
                    (Node.Server serverWallet.NodeServer).CreateRecoveryTxForForceClose
                        channelId
                        closingTx.Tx
            | _ ->
                do! Async.Sleep 2000
                return! waitForRemoteForceClose()
        }
        let! recoveryTxOpt = waitForRemoteForceClose()
        let recoveryTx = UnwrapResult recoveryTxOpt "no funds could be recovered"
        let! _recoveryTxId =
            ChannelManager.BroadcastRecoveryTxAndCloseChannel recoveryTx serverWallet.ChannelStore

        Infrastructure.LogDebug ("waiting for our wallet balance to increase")
        let! _balanceAfterFundsReclaimed =
            let amount = balanceBeforeFundsReclaimed+ Money(1.0m, MoneyUnit.Satoshi)
            serverWallet.WaitForBalance amount

        (serverWallet :> IDisposable).Dispose()
    }

    [<Category "G2G_ChannelRemoteForceClosingByFundee_Funder">]
    [<Test>]
    member __.``can send htlc payments and handle remote force-close of channel by fundee (funder)``() = Helpers.RunAsyncTest <| async {
        let! channelId, clientWallet, bitcoind, electrumServer, lnd, _fundingAmount =
            try
                OpenChannelWithFundee (Some Config.FundeeNodeEndpoint)
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: channel remote-force-closing inconclusive because Channel open failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"

        try
            let! sendPaymentRes = SendHtlcPaymentToGW channelId clientWallet "invoice.txt"
            UnwrapResult sendPaymentRes "sending htlc failed."
        with
        | ex ->
            Assert.Fail (
                sprintf
                    "Inconclusive: channel remote-force-closing inconclusive because sending of htlc payments failed, fix this first: %s"
                    (ex.ToString())
            )
            failwith "unreachable"

        // wait for force-close transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500

        let! balanceBeforeFundsReclaimed = clientWallet.GetBalance()

        // Mine the force-close tx into a block
        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

        let! closingInfoOpt = clientWallet.ChannelStore.CheckForClosingTx channelId
        let closingTx, _closingTxConfirmationsOpt = UnwrapOption closingInfoOpt "force close tx not found on blockchain"

        let forceCloseTx =
            match closingTx with
            | ClosingTx.ForceClose forceCloseTx ->
                forceCloseTx
            | _ -> failwith "closing tx is not a force close tx"

        let! recoveryTxOpt =
            (Node.Client clientWallet.NodeClient).CreateRecoveryTxForForceClose
                channelId
                forceCloseTx.Tx
        let recoveryTx = UnwrapResult recoveryTxOpt "no funds could be recovered"
        let! _recoveryTxId =
            ChannelManager.BroadcastRecoveryTxAndCloseChannel recoveryTx clientWallet.ChannelStore

        // wait for our recovery tx to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500

        // Mine our recovery tx
        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

        Infrastructure.LogDebug ("waiting for our wallet balance to increase")
        let! _balanceAfterFundsReclaimed =
            let amount = balanceBeforeFundsReclaimed+ Money(1.0m, MoneyUnit.Satoshi)
            clientWallet.WaitForBalance amount

        // wait for fundee's recovery tx to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500

        // Mine the fundee's recovery tx
        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

        // Give the fundee time to see their funds recovered before closing bitcoind/electrum
        do! Async.Sleep 20000

        TearDown clientWallet bitcoind electrumServer lnd
    }

    [<Category "G2G_ChannelRemoteForceClosingByFundee_Fundee">]
    [<Test>]
    member __.``can receive htlc payments and handle remote force-close of channel by fundee (fundee)``() = Helpers.RunAsyncTest <| async {
        let! serverWallet, channelId =
            try
                AcceptChannelFromGeewalletFunder ()
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: channel remote-force-closing inconclusive because Channel accept failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"

        try
            do! ReceiveHtlcPaymentToGW channelId serverWallet "invoice.txt" |> Async.Ignore
        with
        | ex ->
            Assert.Fail (
                sprintf
                    "Inconclusive: channel remote-force-closing inconclusive because receiving of htlc payments failed, fix this first: %s"
                    (ex.ToString())
            )
            failwith "unreachable"

        let! _forceCloseTxId = (Node.Server serverWallet.NodeServer).ForceCloseChannel channelId

        let locallyForceClosedData =
            match (serverWallet.ChannelStore.ChannelInfo channelId).Status with
            | ChannelStatus.LocallyForceClosed locallyForceClosedData ->
                locallyForceClosedData
            | status -> failwith (SPrintF1 "unexpected channel status. Expected LocallyForceClosed, got %A" status)

        Infrastructure.LogDebug (SPrintF1 "the time lock is %i blocks" locallyForceClosedData.ToSelfDelay)

        let! balanceBeforeFundsReclaimed = serverWallet.GetBalance()

        let rec waitForTimeLockExpired() = async {
            let! remainingConfirmations = locallyForceClosedData.GetRemainingConfirmations()
            if remainingConfirmations > 0us then
                do! Async.Sleep 500
                return! waitForTimeLockExpired()
        }
        do! waitForTimeLockExpired()

        let! spendingTxResult =
            let commitmentTx = serverWallet.ChannelStore.GetCommitmentTx channelId
            (Lightning.Node.Server serverWallet.NodeServer).CreateRecoveryTxForForceClose
                channelId
                commitmentTx

        let recoveryTx = UnwrapResult spendingTxResult "Local output is dust, recovery tx cannot be created"

        let! _recoveryTxId =
            ChannelManager.BroadcastRecoveryTxAndCloseChannel recoveryTx serverWallet.ChannelStore

        Infrastructure.LogDebug "waiting for our wallet balance to increase"
        let! _balanceAfterFundsReclaimed =
            let amount = balanceBeforeFundsReclaimed + Money(1.0m, MoneyUnit.Satoshi)
            serverWallet.WaitForBalance amount

        (serverWallet :> IDisposable).Dispose()
    }

    [<Category "G2G_Revocation_Funder">]
    [<Test>]
    member __.``can revoke commitment tx (funder)``() = Async.RunSynchronously <| async {
        let! walletInstance = ClientWalletInstance.New None
        let! bitcoind = Bitcoind.Start()
        let! electrumServer = ElectrumServer.Start bitcoind
        let! lnd = Lnd.Start bitcoind

        // As explained in the other test, geewallet cannot use coinbase outputs.
        // To work around that we mine a block to a LND instance and afterwards tell
        // it to send funds to the funder geewallet instance
        let! lndAddress = lnd.GetDepositAddress()
        let blocksMinedToLnd = BlockHeightOffset32 1u
        bitcoind.GenerateBlocks blocksMinedToLnd lndAddress

        let maturityDurationInNumberOfBlocks = BlockHeightOffset32 (uint32 NBitcoin.Consensus.RegTest.CoinbaseMaturity)
        bitcoind.GenerateBlocks maturityDurationInNumberOfBlocks lndAddress

        // We confirm the one block mined to LND, by waiting for LND to see the chain
        // at a height which has that block matured. The height at which the block will
        // be matured is 100 on regtest. Since we initialally mined one block for LND,
        // this will wait until the block height of LND reaches 1 (initial blocks mined)
        // plus 100 blocks (coinbase maturity). This test has been parameterized
        // to use the constants defined in NBitcoin, but you have to keep in mind that
        // the coinbase maturity may be defined differently in other coins.
        do! lnd.WaitForBlockHeight (BlockHeight.Zero + blocksMinedToLnd + maturityDurationInNumberOfBlocks)
        do! lnd.WaitForBalance (Money(50UL, MoneyUnit.BTC))

        // fund geewallet
        let geewalletAccountAmount = Money (25m, MoneyUnit.BTC)
        let! feeRate = ElectrumServer.EstimateFeeRate()
        let! _txid = lnd.SendCoins geewalletAccountAmount walletInstance.Address feeRate

        // wait for lnd's transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            Thread.Sleep 500

        // We want to make sure Geewallet consideres the money received.
        // A typical number of blocks that is almost universally considered
        // 100% confirmed, is 6. Therefore we mine 7 blocks. Because we have
        // waited for the transaction to appear in bitcoind's mempool, we
        // can assume that the first of the 7 blocks will include the
        // transaction sending money to Geewallet. The next 6 blocks will
        // bury the first block, so that the block containing the transaction
        // will be 6 deep at the end of the following call to generateBlocks.
        // At that point, the 0.25 regtest coins from the above call to sendcoins
        // are considered arrived to Geewallet.
        let consideredConfirmedAmountOfBlocksPlusOne = BlockHeightOffset32 7u
        bitcoind.GenerateBlocks consideredConfirmedAmountOfBlocksPlusOne lndAddress

        let fundingAmount = Money(0.1m, MoneyUnit.BTC)
        let! transferAmount = async {
            let! accountBalance = walletInstance.WaitForBalance fundingAmount
            return TransferAmount (fundingAmount.ToDecimal MoneyUnit.BTC, accountBalance.ToDecimal MoneyUnit.BTC, Currency.BTC)
        }
        let! metadata = ChannelManager.EstimateChannelOpeningFee (walletInstance.Account :?> NormalUtxoAccount) transferAmount
        let! pendingChannelRes =
            Lightning.Network.OpenChannel
                walletInstance.NodeClient
                (NodeIdentifier.TcpEndPoint Config.FundeeNodeEndpoint)
                transferAmount
        let pendingChannel = UnwrapResult pendingChannelRes "OpenChannel failed"
        let minimumDepth = (pendingChannel :> IChannelToBeOpened).ConfirmationsRequired
        let! acceptRes = pendingChannel.Accept metadata walletInstance.Password
        let (channelId, _fundingTxId) = UnwrapResult acceptRes "pendingChannel.Accept failed"
        bitcoind.GenerateBlocks (BlockHeightOffset32 minimumDepth) lndAddress

        do!
            let channelInfo = walletInstance.ChannelStore.ChannelInfo channelId
            let fundingBroadcastButNotLockedData =
                match channelInfo.Status with
                | ChannelStatus.FundingBroadcastButNotLocked fundingBroadcastButNotLockedData
                    -> fundingBroadcastButNotLockedData
                | status -> failwith (SPrintF1 "Unexpected channel status. Expected FundingBroadcastButNotLocked, got %A" status)
            let rec waitForFundingConfirmed() = async {
                let! remainingConfirmations = fundingBroadcastButNotLockedData.GetRemainingConfirmations()
                if remainingConfirmations > 0u then
                    do! Async.Sleep 1000
                    return! waitForFundingConfirmed()
                else
                    // TODO: the backend API doesn't give us any way to avoid
                    // the FundingOnChainLocationUnknown error, so just sleep
                    // to avoid the race condition. This waiting should really
                    // be implemented on the backend anyway.
                    do! Async.Sleep 10000
                    return ()
            }
            waitForFundingConfirmed()

        let! lockFundingRes = Lightning.Network.ConnectLockChannelFunding walletInstance.NodeClient channelId
        UnwrapResult lockFundingRes "LockChannelFunding failed"

        let channelInfo = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfo.Balance, MoneyUnit.BTC) <> fundingAmount then
            return failwith "balance does not match funding amount"

        let! sendPayment1Res = SendHtlcPaymentToGW channelId walletInstance "invoice-1.txt"
        UnwrapResult sendPayment1Res "sending htlc failed."

        let channelInfoAfterPayment1 = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfoAfterPayment1.Balance, MoneyUnit.BTC) <> fundingAmount - walletToWalletTestPayment1Amount then
            return failwith "incorrect balance after payment 1"

        let commitmentTx = walletInstance.ChannelStore.GetCommitmentTx channelId

        let! sendPayment2Res = SendHtlcPaymentToGW channelId walletInstance "invoice-2.txt"
        UnwrapResult sendPayment2Res "sending htlc failed."

        let channelInfoAfterPayment2 = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfoAfterPayment2.Balance, MoneyUnit.BTC) <> fundingAmount - (2 * walletToWalletTestPayment1Amount) then
            return failwith "incorrect balance after payment 1"

        let! _theftTxId = UtxoCoin.Account.BroadcastRawTransaction Currency.BTC (commitmentTx.ToString())

        // wait for theft transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500

        // mine the theft tx into a block
        bitcoind.GenerateBlocks (BlockHeightOffset32 1u) lndAddress

        let! accountBalanceBeforeSpendingTheftTx =
            walletInstance.GetBalance()

        // give the fundee plenty of time to broadcast the penalty tx
        do! Async.Sleep 20000

        // mine enough blocks to allow broadcasting the spending tx
        let toSelfDelay = walletInstance.ChannelStore.GetToSelfDelay channelId
        bitcoind.GenerateBlocks (BlockHeightOffset32 (uint32 toSelfDelay)) lndAddress

        let! spendingTxRes =
            (Node.Client walletInstance.NodeClient).CreateRecoveryTxForForceClose
                channelId
                commitmentTx
        let spendingTx = UnwrapResult spendingTxRes "failed to create spending tx"
        let! spendingTxIdOpt = async {
            try
                let! spendingTxId = ChannelManager.BroadcastRecoveryTxAndCloseChannel spendingTx walletInstance.ChannelStore
                return Some spendingTxId
            with
            | ex ->
                let _exns = FindSingleException<UtxoCoin.ElectrumServerReturningErrorException> ex
                return None
        }

        match spendingTxIdOpt with
        | Some spendingTxId ->
            return failwith (SPrintF1 "successfully broadcast spending tx (%s) - revokation didn't prevent fraud" spendingTxId)
        | None -> ()

        let! accountBalanceAfterSpendingTheftTx =
            walletInstance.GetBalance()

        if accountBalanceBeforeSpendingTheftTx <> accountBalanceAfterSpendingTheftTx then
            return failwithf
                "Unexpected account balance! before theft tx == %A, after theft tx == %A"
                accountBalanceBeforeSpendingTheftTx
                accountBalanceAfterSpendingTheftTx

        // give the fundee plenty of time to see that their tx was mined
        do! Async.Sleep 20000

        TearDown walletInstance bitcoind electrumServer lnd

        return ()
    }

    [<Category "G2G_Revocation_Fundee">]
    [<Test>]
    member __.``can revoke commitment tx (fundee)``() = Helpers.RunAsyncTest <| async {
        use! walletInstance = ServerWalletInstance.New Config.FundeeLightningIPEndpoint (Some Config.FundeeAccountsPrivateKey)
        let! pendingChannelRes =
            Lightning.Network.AcceptChannel
                walletInstance.NodeServer

        let (channelId, _) = UnwrapResult pendingChannelRes "OpenChannel failed"

        let! lockFundingRes = Lightning.Network.AcceptLockChannelFunding walletInstance.NodeServer channelId
        UnwrapResult lockFundingRes "LockChannelFunding failed"

        let channelInfo = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfo.Balance, MoneyUnit.BTC) <> Money(0.0m, MoneyUnit.BTC) then
            return failwith "incorrect balance after accepting channel"

        do! ReceiveHtlcPaymentToGW channelId walletInstance "invoice-1.txt" |> Async.Ignore

        let channelInfoAfterPayment1 = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfoAfterPayment1.Balance, MoneyUnit.BTC) <> walletToWalletTestPayment1Amount then
            return failwith "incorrect balance after receiving payment 1"

        do! ReceiveHtlcPaymentToGW channelId walletInstance "invoice-2.txt" |> Async.Ignore

        let channelInfoAfterPayment2 = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfoAfterPayment2.Balance, MoneyUnit.BTC) <> (2 * walletToWalletTestPayment1Amount) then
            return failwith "incorrect balance after receiving payment 2"

        // attempt to broadcast tx which spends the theft tx
        let rec checkForClosingTx() = async {
            let! txIdOpt =
                (Node.Server walletInstance.NodeServer).CheckForChannelFraudAndSendRevocationTx
                    channelId
            match txIdOpt with
            | None ->
                do! Async.Sleep 500
                return! checkForClosingTx()
            | Some _ ->
                return ()
        }
        do! checkForClosingTx()
        let! _accountBalance =
            // wait for any amount of money to appear in the wallet
            let amount = Money(1.0m, MoneyUnit.Satoshi)
            walletInstance.WaitForBalance amount

        return ()
    }

    [<Category "G2G_CPFP_Funder">]
    [<Test>]
    member __.``CPFP is used when force-closing channel (funder)``() = Helpers.RunAsyncTest <| async {
        let! channelId, clientWallet, bitcoind, electrumServer, lnd, _fundingAmount =
            try
                OpenChannelWithFundee (Some Config.FundeeNodeEndpoint)
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: CPFP inconclusive because Channel open failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"

        try
            let! sendPaymentRes = SendHtlcPaymentToGW channelId clientWallet "invoice.txt"
            UnwrapResult sendPaymentRes "sending htlc failed."
        with
        | ex ->
            Assert.Fail (
                sprintf
                    "Inconclusive: CPFP inconclusive because sending of htlc payments failed, fix this first: %s"
                    (ex.ToString())
            )
            failwith "unreachable"

        let! oldFeeRate = ElectrumServer.EstimateFeeRate()

        let newFeeRate = oldFeeRate * 5u
        ElectrumServer.SetEstimatedFeeRate newFeeRate

        let wrappedCommitmentTx = clientWallet.ChannelStore.GetCommitmentTx channelId
        let! _commitmentTxIdString = Account.BroadcastRawTransaction Currency.BTC (wrappedCommitmentTx.ToString())

        let commitmentTx = Transaction.Parse(wrappedCommitmentTx.ToString(), Network.RegTest)
        let! commitmentTxFee = FeesHelper.GetFeeFromTransaction commitmentTx
        let commitmentTxFeeRate =
            FeeRatePerKw.FromFeeAndVSize(commitmentTxFee, uint64 (commitmentTx.GetVirtualSize()))
        assert FeesHelper.FeeRatesApproxEqual commitmentTxFeeRate oldFeeRate

        let! anchorTxRes =
            (Node.Client clientWallet.NodeClient).CreateAnchorFeeBumpForForceClose
                channelId
                wrappedCommitmentTx
                clientWallet.Password
        let anchorTxString =
            UnwrapResult
                anchorTxRes
                "force close failed to recover funds from the commitment tx"
        let anchorTx = Transaction.Parse(anchorTxString.Tx.ToString(), Network.RegTest)
        let! anchorTxFee = FeesHelper.GetFeeFromTransaction anchorTx
        let anchorTxFeeRate =
            FeeRatePerKw.FromFeeAndVSize(anchorTxFee, uint64 (anchorTx.GetVirtualSize()))
        assert (not <| FeesHelper.FeeRatesApproxEqual anchorTxFeeRate oldFeeRate)
        assert (not <| FeesHelper.FeeRatesApproxEqual anchorTxFeeRate newFeeRate)
        let combinedFeeRate =
            FeeRatePerKw.FromFeeAndVSize(
                anchorTxFee + commitmentTxFee,
                uint64 (anchorTx.GetVirtualSize() + commitmentTx.GetVirtualSize())
            )
        assert FeesHelper.FeeRatesApproxEqual combinedFeeRate newFeeRate

        let! _anchorTxIdString = Account.BroadcastRawTransaction Currency.BTC (anchorTxString.Tx.ToString())

        // Give the fundee time to see the force-close tx
        do! Async.Sleep 5000

        TearDown clientWallet bitcoind electrumServer lnd
    }

    [<Category "G2G_CPFP_Fundee">]
    [<Test>]
    member __.``CPFP is used when force-closing channel (fundee)``() = Helpers.RunAsyncTest <| async {
        let! serverWallet, channelId =
            try
                AcceptChannelFromGeewalletFunder ()
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: channel remote-force-closing inconclusive because Channel accept failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"

        try
            do! ReceiveHtlcPaymentToGW channelId serverWallet "invoice.txt" |> Async.Ignore
        with
        | ex ->
            Assert.Fail (
                sprintf
                    "Inconclusive: CPFP inconclusive because receiving of htlc payments failed, fix this first: %s"
                    (ex.ToString())
            )
            failwith "unreachable"

        let! oldFeeRate = ElectrumServer.EstimateFeeRate()
        let newFeeRate = oldFeeRate * 5u
        ElectrumServer.SetEstimatedFeeRate newFeeRate

        let rec waitForForceClose(): Async<ForceCloseTx> = async {
            let! closingTxInfoOpt = serverWallet.ChannelStore.CheckForClosingTx channelId
            match closingTxInfoOpt with
            | Some (ClosingTx.ForceClose forceCloseTx, _closingTxConfirmations) ->
                return forceCloseTx
            | Some (ClosingTx.MutualClose _mutualCloseTx, _closingTxConfirmations) ->
                return failwith "should not happen"
            | None ->
                do! Async.Sleep 500
                return! waitForForceClose()
        }

        let! wrappedForceCloseTx = waitForForceClose()
        let forceCloseTxString = wrappedForceCloseTx.Tx.ToString()
        let forceCloseTx = Transaction.Parse (forceCloseTxString, Network.RegTest)
        let! forceCloseTxFee = FeesHelper.GetFeeFromTransaction forceCloseTx
        let forceCloseTxFeeRate =
            FeeRatePerKw.FromFeeAndVSize(forceCloseTxFee, uint64 (forceCloseTx.GetVirtualSize()))
        assert FeesHelper.FeeRatesApproxEqual forceCloseTxFeeRate oldFeeRate

        (serverWallet :> IDisposable).Dispose()
    }


    [<Category "G2G_HtlcPayment_Funder">]
    [<Test>]
    member __.``can send htlc payments, with geewallet (funder)``() = Helpers.RunAsyncTest <| async {
        let! channelId, clientWallet, bitcoind, electrumServer, lnd, fundingAmount =
            try
                OpenChannelWithFundee (Some Config.FundeeNodeEndpoint)
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: htlc-sending inconclusive because Channel open failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"

        let channelInfoBeforeAnyPayment = clientWallet.ChannelStore.ChannelInfo channelId
        match channelInfoBeforeAnyPayment.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        let! sendHtlcPayment1Res =
            SendHtlcPaymentToGW channelId clientWallet "invoice.txt"

        UnwrapResult sendHtlcPayment1Res "SendHtlcPayment failed"

        let channelInfoAfterPayment1 = clientWallet.ChannelStore.ChannelInfo channelId
        match channelInfoAfterPayment1.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfoAfterPayment1.Balance, MoneyUnit.BTC) <> fundingAmount - walletToWalletTestPayment1Amount then
            return failwith "incorrect balance after payment 1"

        TearDown clientWallet bitcoind electrumServer lnd
    }


    [<Category "G2G_HtlcPayment_Fundee">]
    [<Test>]
    member __.``can receive htlc payments, with geewallet (fundee)``() = Helpers.RunAsyncTest <| async {
        let! serverWallet, channelId = AcceptChannelFromGeewalletFunder ()

        let channelInfoBeforeAnyPayment = serverWallet.ChannelStore.ChannelInfo channelId
        match channelInfoBeforeAnyPayment.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        let balanceBeforeAnyPayment = Money(channelInfoBeforeAnyPayment.Balance, MoneyUnit.BTC)

        let! _htlcStatus =
            ReceiveHtlcPaymentToGW channelId serverWallet "invoice.txt"

        let channelInfoAfterPayment1 = serverWallet.ChannelStore.ChannelInfo channelId
        match channelInfoAfterPayment1.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfoAfterPayment1.Balance, MoneyUnit.BTC) <> balanceBeforeAnyPayment + walletToWalletTestPayment1Amount then
            return failwith "incorrect balance after receiving payment 1"

        (serverWallet :> IDisposable).Dispose()
    }

    [<Category "G2G_HtlcPaymentRevocationCloseByFunder_Funder">]
    [<Test>]
    member __.``can send htlc payments with revocation (close by funder), with geewallet (funder)``() = Helpers.RunAsyncTest <| async {
        let invoiceFileFromFundeePath =
            Path.Combine (Path.GetTempPath(), "invoice-1.txt")
        let secondInvoiceFileFromFundeePath =
            Path.Combine (Path.GetTempPath(), "invoice-2.txt")

        let rec readInvoice (path: string) =
            async {
                try
                    let invoiceString = File.ReadAllText path
                    if String.IsNullOrWhiteSpace invoiceString then
                        do! Async.Sleep 500
                        return! readInvoice path
                    else
                        return invoiceString
                with
                | :? FileNotFoundException ->
                    do! Async.Sleep 500
                    return! readInvoice path
            }

        // Clear the invoice file from previous runs
        File.Delete (invoiceFileFromFundeePath)
        File.Delete (secondInvoiceFileFromFundeePath)

        let! channelId, clientWallet, bitcoind, electrumServer, lnd, _fundingAmount =
            try
                OpenChannelWithFundee (Some Config.FundeeNodeEndpoint)
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: htlc-sending inconclusive because Channel open failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"

        let channelInfoBeforeAnyPayment = clientWallet.ChannelStore.ChannelInfo channelId
        match channelInfoBeforeAnyPayment.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        let! sendHtlcPayment1Res =
            async {
                let! invoiceInString =
                    readInvoice invoiceFileFromFundeePath

                return!
                    Lightning.Network.SendHtlcPayment
                        clientWallet.NodeClient
                        channelId
                        (PaymentInvoice.Parse invoiceInString)
                        false
            }
        UnwrapResult sendHtlcPayment1Res "SendHtlcPayment failed"

        let commitmentTx = clientWallet.ChannelStore.GetCommitmentTx channelId

        let! sendHtlcPayment2Res =
            async {
                let! invoiceInString =
                    readInvoice secondInvoiceFileFromFundeePath

                return!
                    Lightning.Network.SendHtlcPayment
                        clientWallet.NodeClient
                        channelId
                        (PaymentInvoice.Parse invoiceInString)
                        false
            }
        UnwrapResult sendHtlcPayment2Res "SendHtlcPayment failed"

        let! _theftTxId = UtxoCoin.Account.BroadcastRawTransaction Currency.BTC (commitmentTx.ToString())

        // wait for force-close transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500

        // Mine the force-close tx into a block
        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

        let rec waitForClosingTx () =
            async {
                Console.WriteLine "Looking for closing tx"
                let! result = ChainWatcher.CheckForChannelForceCloseAndSaveUnresolvedHtlcs channelId clientWallet.ChannelStore
                if result then
                    return ()
                else
                    do! Async.Sleep 500
                    return! waitForClosingTx()
            }

        do! waitForClosingTx ()

        let rec waitUntilReadyForBroadcastIsNotEmpty () =
            async {
                let! _ = ChainWatcher.CheckForChannelForceCloseAndSaveUnresolvedHtlcs channelId clientWallet.ChannelStore
                let! readyForBroadcast = ChainWatcher.CheckForChannelReadyToBroadcastHtlcTransactions channelId clientWallet.ChannelStore
                if readyForBroadcast.IsDone () then
                    return readyForBroadcast
                else if readyForBroadcast.IsEmpty () then
                    Console.WriteLine "No ready for broadcast, rechecking"
                    bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)
                    do! Async.Sleep 1000
                    return! waitUntilReadyForBroadcastIsNotEmpty ()
                else
                    return readyForBroadcast
            }

        let! readyToBroadcastHtlcTxs = waitUntilReadyForBroadcastIsNotEmpty()

        let rec broadcastUntilListIsEmpty (readyToBroadcastList: HtlcTxsList) (feeSum: Money) =
            async {
                if readyToBroadcastList.IsEmpty() then
                    return feeSum
                else
                    let! htlcTx, rest = (Lightning.Node.Client clientWallet.NodeClient).CreateHtlcTxForHtlcTxsList readyToBroadcastHtlcTxs clientWallet.Password
                    Console.WriteLine (sprintf "Broadcasting... %s" (htlcTx.Tx.ToString()))

                    do! ChannelManager.BroadcastHtlcTxAndAddToWatchList htlcTx clientWallet.ChannelStore |> Async.Ignore
                    bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

                    return! broadcastUntilListIsEmpty rest (feeSum + (Money.Satoshis htlcTx.Fee.EstimatedFeeInSatoshis))
            }

        let! _feesPaidFor2ndStageHtlcTx = broadcastUntilListIsEmpty readyToBroadcastHtlcTxs Money.Zero

        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 10u)
        do! Async.Sleep (10000)

        TearDown clientWallet bitcoind electrumServer lnd
    }


    [<Category "G2G_HtlcPaymentRevocationCloseByFunder_Fundee">]
    [<Test>]
    member __.``can receive htlc payments with revocation (close by funder), with geewallet (fundee)``() = Helpers.RunAsyncTest <| async {
        let! serverWallet, channelId = AcceptChannelFromGeewalletFunder ()

        let channelInfoBeforeAnyPayment = serverWallet.ChannelStore.ChannelInfo channelId
        match channelInfoBeforeAnyPayment.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        let invoiceManager = InvoiceManagement (serverWallet.Account :?> NormalUtxoAccount, serverWallet.Password)
        let amountInSatoshis =
            Convert.ToUInt64 walletToWalletTestPayment1Amount.Satoshi
        let invoice1InString = invoiceManager.CreateInvoice amountInSatoshis "Payment 1"
        let invoice2InString = invoiceManager.CreateInvoice amountInSatoshis "Payment 2"

        File.WriteAllText (Path.Combine (Path.GetTempPath(), "invoice-1.txt"), invoice1InString)
        File.WriteAllText (Path.Combine (Path.GetTempPath(), "invoice-2.txt"), invoice2InString)

        let! receiveHtlcPaymentRes =
            Lightning.Network.ReceiveLightningEvent serverWallet.NodeServer channelId false
        let receiveHtlcPayment = UnwrapResult receiveHtlcPaymentRes "ReceiveHtlcPayment failed"

        match receiveHtlcPayment with
        | HtlcPayment _status ->
            let channelInfoAfterPayment1 = serverWallet.ChannelStore.ChannelInfo channelId
            match channelInfoAfterPayment1.Status with
            | ChannelStatus.Active -> ()
            | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

            let! receiveHtlcPaymentRes =
                Lightning.Network.ReceiveLightningEvent serverWallet.NodeServer channelId false
            let receiveHtlcPayment = UnwrapResult receiveHtlcPaymentRes "ReceiveHtlcPayment failed"
            match receiveHtlcPayment with
            | HtlcPayment _status ->
                try
                    let channelInfoAfterPayment1 = serverWallet.ChannelStore.ChannelInfo channelId
                    match channelInfoAfterPayment1.Status with
                    | ChannelStatus.Active -> ()
                    | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

                    let! balanceBeforeFundsReclaimed = serverWallet.GetBalance()

                    let rec waitForClosingTx () =
                        async {
                            Console.WriteLine "Looking for closing tx"
                            let! result = ChainWatcher.CheckForChannelForceCloseAndSaveUnresolvedHtlcs channelId serverWallet.ChannelStore
                            if result then
                                return ()
                            else
                                do! Async.Sleep 500
                                return! waitForClosingTx()
                        }

                    do! waitForClosingTx ()

                    let rec waitUntilReadyForBroadcastIsNotEmpty () =
                        async {
                            let! _result = ChainWatcher.CheckForChannelForceCloseAndSaveUnresolvedHtlcs channelId serverWallet.ChannelStore
                            let! readyForBroadcast = ChainWatcher.CheckForChannelReadyToBroadcastHtlcTransactions channelId serverWallet.ChannelStore
                            if readyForBroadcast.IsDone () then
                                return readyForBroadcast
                            else if readyForBroadcast.IsEmpty () then
                                Console.WriteLine "No ready for broadcast, rechecking"
                                do! Async.Sleep 100
                                return! waitUntilReadyForBroadcastIsNotEmpty ()
                            else
                                return readyForBroadcast
                        }

                    let! readyToBroadcastHtlcTxs = waitUntilReadyForBroadcastIsNotEmpty()

                    let rec broadcastUntilListIsEmpty (readyToBroadcastList: HtlcTxsList) (feeSum: Money) =
                        async {
                            if readyToBroadcastList.IsEmpty() then
                                return feeSum
                            else
                                let! htlcTx, rest = (Lightning.Node.Server serverWallet.NodeServer).CreateHtlcTxForHtlcTxsList readyToBroadcastHtlcTxs serverWallet.Password
                                Console.WriteLine (sprintf "Broadcasting... %s" (htlcTx.Tx.ToString()))
                                do! ChannelManager.BroadcastHtlcTxAndAddToWatchList htlcTx serverWallet.ChannelStore |> Async.Ignore

                                return! broadcastUntilListIsEmpty rest (feeSum + (Money.Satoshis htlcTx.Fee.EstimatedFeeInSatoshis))
                        }

                    let! feesPaid = broadcastUntilListIsEmpty readyToBroadcastHtlcTxs Money.Zero

                    do! serverWallet.WaitForBalance (balanceBeforeFundsReclaimed + walletToWalletTestPayment1Amount - feesPaid) |> Async.Ignore
                with
                | ex -> Console.WriteLine (ex.ToString())
            | _ ->
                Assert.Fail "received non-htlc lightning event"
        | _ ->
            Assert.Fail "received non-htlc lightning event"

        (serverWallet :> IDisposable).Dispose()
    }

    [<Category "G2G_HtlcPaymentRevocationCloseByFundee_Funder">]
    [<Test>]
    member __.``can send htlc payments with revocation (close by fundee), with geewallet (funder)``() = Helpers.RunAsyncTest <| async {
        let invoiceFileFromFundeePath =
            Path.Combine (Path.GetTempPath(), "invoice-1.txt")
        let secondInvoiceFileFromFundeePath =
            Path.Combine (Path.GetTempPath(), "invoice-2.txt")
        let fundeeWalletAddressPath =
            Path.Combine (Path.GetTempPath(), "address.txt")

        let rec readInvoice (path: string) =
            async {
                try
                    let invoiceString = File.ReadAllText path
                    if String.IsNullOrWhiteSpace invoiceString then
                        do! Async.Sleep 500
                        return! readInvoice path
                    else
                        return invoiceString
                with
                | :? FileNotFoundException ->
                    do! Async.Sleep 500
                    return! readInvoice path
            }

        // Clear the invoice file from previous runs
        File.Delete (invoiceFileFromFundeePath)
        File.Delete (secondInvoiceFileFromFundeePath)
        File.Delete (fundeeWalletAddressPath)

        let! channelId, clientWallet, bitcoind, electrumServer, lnd, _fundingAmount =
            try
                OpenChannelWithFundee (Some Config.FundeeNodeEndpoint)
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: htlc-sending inconclusive because Channel open failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"

        let channelInfoBeforeAnyPayment = clientWallet.ChannelStore.ChannelInfo channelId
        match channelInfoBeforeAnyPayment.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        let! fundeeWalletAddress =
            readInvoice fundeeWalletAddressPath

        // fund geewallet
        let geewalletAccountAmount = Money (10m, MoneyUnit.BTC)
        let! feeRate = ElectrumServer.EstimateFeeRate()
        let! _txid = lnd.SendCoins geewalletAccountAmount (BitcoinScriptAddress(fundeeWalletAddress, Network.RegTest)) feeRate

        // wait for lnd's transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            Thread.Sleep 500

        // We want to make sure Geewallet considers the money received.
        // A typical number of blocks that is almost universally considered
        // 100% confirmed, is 6. Therefore we mine 7 blocks. Because we have
        // waited for the transaction to appear in bitcoind's mempool, we
        // can assume that the first of the 7 blocks will include the
        // transaction sending money to Geewallet. The next 6 blocks will
        // bury the first block, so that the block containing the transaction
        // will be 6 deep at the end of the following call to generateBlocks.
        // At that point, the 0.25 regtest coins from the above call to sendcoins
        // are considered arrived to Geewallet.
        let consideredConfirmedAmountOfBlocksPlusOne = BlockHeightOffset32 7u
        bitcoind.GenerateBlocksToDummyAddress consideredConfirmedAmountOfBlocksPlusOne

        let! sendHtlcPayment1Res =
            async {
                let! invoiceInString =
                    readInvoice invoiceFileFromFundeePath

                return!
                    Lightning.Network.SendHtlcPayment
                        clientWallet.NodeClient
                        channelId
                        (PaymentInvoice.Parse invoiceInString)
                        false
            }
        UnwrapResult sendHtlcPayment1Res "SendHtlcPayment failed"

        let! sendHtlcPayment2Res =
            async {
                let! invoiceInString =
                    readInvoice secondInvoiceFileFromFundeePath

                return!
                    Lightning.Network.SendHtlcPayment
                        clientWallet.NodeClient
                        channelId
                        (PaymentInvoice.Parse invoiceInString)
                        false
            }
        UnwrapResult sendHtlcPayment2Res "SendHtlcPayment failed"

        // wait for force-close transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500

        // Mine the force-close tx into a block
        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

        let! balanceBeforeRevocation = clientWallet.GetBalance()

        let rec waitForClosingTx () =
            async {
                Console.WriteLine "Looking for closing tx"
                let! result = ChainWatcher.CheckForChannelForceCloseAndSaveUnresolvedHtlcs channelId clientWallet.ChannelStore
                if result then
                    return ()
                else
                    do! Async.Sleep 500
                    return! waitForClosingTx()
            }

        do! waitForClosingTx ()

        let rec waitUntilReadyForBroadcastIsNotEmpty () =
            async {
                let! readyForBroadcast = ChainWatcher.CheckForChannelReadyToBroadcastHtlcTransactions channelId clientWallet.ChannelStore
                if readyForBroadcast.IsDone () then
                    return readyForBroadcast
                else if readyForBroadcast.IsEmpty () then
                    Console.WriteLine "No ready for broadcast, rechecking"
                    do! Async.Sleep 1000
                    return! waitUntilReadyForBroadcastIsNotEmpty ()
                else
                    return readyForBroadcast
            }

        let! readyToBroadcastHtlcTxs = waitUntilReadyForBroadcastIsNotEmpty()

        //Wait for fundee to broadcast success tx
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500

        // Mine the 2nd stage tx into a block
        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

        let rec broadcastUntilListIsEmpty (readyToBroadcastList: HtlcTxsList) (feeSum: Money) =
            async {
                if readyToBroadcastList.IsEmpty() then
                    return feeSum
                else
                    let! htlcTx, rest = (Lightning.Node.Client clientWallet.NodeClient).CreateHtlcTxForHtlcTxsList readyToBroadcastHtlcTxs clientWallet.Password
                    Console.WriteLine (sprintf "Broadcasting... %s" (htlcTx.Tx.ToString()))

                    do! ChannelManager.BroadcastHtlcTxAndAddToWatchList htlcTx clientWallet.ChannelStore |> Async.Ignore
                    bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

                    return! broadcastUntilListIsEmpty rest (feeSum + (Money.Satoshis htlcTx.Fee.EstimatedFeeInSatoshis))
            }

        let! feesPaidFor2ndStageHtlcTx = broadcastUntilListIsEmpty readyToBroadcastHtlcTxs Money.Zero

        do! clientWallet.WaitForBalance (balanceBeforeRevocation + walletToWalletTestPayment1Amount - feesPaidFor2ndStageHtlcTx) |> Async.Ignore

        TearDown clientWallet bitcoind electrumServer lnd
    }


    [<Category "G2G_HtlcPaymentRevocationCloseByFundee_Fundee">]
    [<Test>]
    member __.``can receive htlc payments with revocation (close by fundee), with geewallet (fundee)``() = Helpers.RunAsyncTest <| async {
        let! serverWallet, channelId = AcceptChannelFromGeewalletFunder ()

        let channelInfoBeforeAnyPayment = serverWallet.ChannelStore.ChannelInfo channelId
        match channelInfoBeforeAnyPayment.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        let invoiceManager = InvoiceManagement (serverWallet.Account :?> NormalUtxoAccount, serverWallet.Password)
        let amountInSatoshis =
            Convert.ToUInt64 walletToWalletTestPayment1Amount.Satoshi
        let invoice1InString = invoiceManager.CreateInvoice amountInSatoshis "Payment 1"
        let invoice2InString = invoiceManager.CreateInvoice amountInSatoshis "Payment 2"

        File.WriteAllText (Path.Combine (Path.GetTempPath(), "invoice-1.txt"), invoice1InString)
        File.WriteAllText (Path.Combine (Path.GetTempPath(), "invoice-2.txt"), invoice2InString)
        File.WriteAllText (Path.Combine (Path.GetTempPath(), "address.txt"), serverWallet.Address.ToString())

        let! receiveHtlcPaymentRes =
            Lightning.Network.ReceiveLightningEvent serverWallet.NodeServer channelId false
        let receiveHtlcPayment = UnwrapResult receiveHtlcPaymentRes "ReceiveHtlcPayment failed"

        match receiveHtlcPayment with
        | HtlcPayment _status ->
            let channelInfoAfterPayment1 = serverWallet.ChannelStore.ChannelInfo channelId
            match channelInfoAfterPayment1.Status with
            | ChannelStatus.Active -> ()
            | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

            let commitmentTx = serverWallet.ChannelStore.GetCommitmentTx channelId

            let! receiveHtlcPaymentRes =
                Lightning.Network.ReceiveLightningEvent serverWallet.NodeServer channelId false
            let receiveHtlcPayment = UnwrapResult receiveHtlcPaymentRes "ReceiveHtlcPayment failed"
            match receiveHtlcPayment with
            | HtlcPayment _status ->
                let channelInfoAfterPayment1 = serverWallet.ChannelStore.ChannelInfo channelId
                match channelInfoAfterPayment1.Status with
                | ChannelStatus.Active -> ()
                | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

                let! _theftTxId = UtxoCoin.Account.BroadcastRawTransaction Currency.BTC (commitmentTx.ToString())

                let rec waitForClosingTx () =
                    async {
                        Console.WriteLine "Looking for closing tx"
                        let! result = ChainWatcher.CheckForChannelForceCloseAndSaveUnresolvedHtlcs channelId serverWallet.ChannelStore
                        if result then
                            return ()
                        else
                            do! Async.Sleep 500
                            return! waitForClosingTx()
                    }

                do! waitForClosingTx ()

                let rec waitUntilReadyForBroadcastIsNotEmpty () =
                    async {
                        let! _result = ChainWatcher.CheckForChannelForceCloseAndSaveUnresolvedHtlcs channelId serverWallet.ChannelStore
                        let! readyForBroadcast = ChainWatcher.CheckForChannelReadyToBroadcastHtlcTransactions channelId serverWallet.ChannelStore
                        if readyForBroadcast.IsDone () then
                            return readyForBroadcast
                        else if readyForBroadcast.IsEmpty () then
                            Console.WriteLine "No ready for broadcast, rechecking"
                            do! Async.Sleep 100
                            return! waitUntilReadyForBroadcastIsNotEmpty ()
                        else
                            return readyForBroadcast
                    }

                let! readyToBroadcastHtlcTxs = waitUntilReadyForBroadcastIsNotEmpty()

                let rec broadcastUntilListIsEmpty (readyToBroadcastList: HtlcTxsList) (feeSum: Money) =
                    async {
                        if readyToBroadcastList.IsEmpty() then
                            return feeSum
                        else
                            let! htlcTx, rest = (Lightning.Node.Server serverWallet.NodeServer).CreateHtlcTxForHtlcTxsList readyToBroadcastHtlcTxs serverWallet.Password
                            Console.WriteLine (sprintf "Broadcasting... %s" (htlcTx.Tx.ToString()))
                            do! ChannelManager.BroadcastHtlcTxAndAddToWatchList htlcTx serverWallet.ChannelStore |> Async.Ignore

                            return! broadcastUntilListIsEmpty rest (feeSum + (Money.Satoshis htlcTx.Fee.EstimatedFeeInSatoshis))
                    }

                let! _feesPaid = broadcastUntilListIsEmpty readyToBroadcastHtlcTxs Money.Zero

                ()
            | _ ->
                Assert.Fail "received non-htlc lightning event"
        | _ ->
            Assert.Fail "received non-htlc lightning event"

        (serverWallet :> IDisposable).Dispose()
    }

    [<Test>]
    member __.``can open channel with LND``() = Helpers.RunAsyncTest <| async {
        let! _channelId, clientWallet, bitcoind, electrumServer, lnd, _fundingAmount = OpenChannelWithFundee None

        TearDown clientWallet bitcoind electrumServer lnd
    }

    [<Category "G2G_ChannelLocalForceClosing_Funder">]
    [<Test>]
    member __.``can send htlc payments and handle local force-close of channel (funder)``() = Helpers.RunAsyncTest <| async {
        let! channelId, clientWallet, bitcoind, electrumServer, lnd, _fundingAmount =
            try
                OpenChannelWithFundee (Some Config.FundeeNodeEndpoint)
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: channel local-force-closing inconclusive because Channel open failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"

        try
            let! sendPaymentRes = SendHtlcPaymentToGW channelId clientWallet "invoice.txt"
            UnwrapResult sendPaymentRes "sending htlc failed."
        with
        | ex ->
            Assert.Fail (
                sprintf
                    "Inconclusive: channel local-force-closing inconclusive because sending of htlc payments failed, fix this first: %s"
                    (ex.ToString())
            )
            failwith "unreachable"

        let! _forceCloseTxId = (Lightning.Node.Client clientWallet.NodeClient).ForceCloseChannel channelId

        let locallyForceClosedData =
            match (clientWallet.ChannelStore.ChannelInfo channelId).Status with
            | ChannelStatus.LocallyForceClosed locallyForceClosedData ->
                locallyForceClosedData
            | status -> failwith (SPrintF1 "unexpected channel status. Expected LocallyForceClosed, got %A" status)

        // wait for force-close transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500

        Infrastructure.LogDebug (SPrintF1 "the time lock is %i blocks" locallyForceClosedData.ToSelfDelay)

        let! balanceBeforeFundsReclaimed = clientWallet.GetBalance()

        // Mine the force-close tx into a block
        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

        // Mine blocks to release time-lock
        bitcoind.GenerateBlocksToDummyAddress
            (BlockHeightOffset32 (uint32 locallyForceClosedData.ToSelfDelay))

        let! spendingTxResult =
            let commitmentTx = clientWallet.ChannelStore.GetCommitmentTx channelId
            (Lightning.Node.Client clientWallet.NodeClient).CreateRecoveryTxForForceClose
                channelId
                commitmentTx

        let recoveryTx = UnwrapResult spendingTxResult "Local output is dust, recovery tx cannot be created"

        let! _recoveryTxId =
            ChannelManager.BroadcastRecoveryTxAndCloseChannel recoveryTx clientWallet.ChannelStore

        // wait for spending transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500

        // Mine the spending tx into a block
        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

        Infrastructure.LogDebug "waiting for our wallet balance to increase"
        let! _balanceAfterFundsReclaimed =
            let amount = balanceBeforeFundsReclaimed + Money(1.0m, MoneyUnit.Satoshi)
            clientWallet.WaitForBalance amount

        TearDown clientWallet bitcoind electrumServer lnd
    }

    [<Category "G2G_ChannelLocalForceClosing_Fundee">]
    [<Test>]
    member __.``can receive htlc payments and handle local force-close of channel (fundee)``() = Helpers.RunAsyncTest <| async {
        let! serverWallet, channelId =
            try
                AcceptChannelFromGeewalletFunder ()
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: channel local-force-closing inconclusive because Channel accept failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"

        try
            do! ReceiveHtlcPaymentToGW channelId serverWallet "invoice.txt" |> Async.Ignore
        with
        | ex ->
            Assert.Fail (
                sprintf
                    "Inconclusive: channel remote-force-closing inconclusive because receiving of htlc payments failed, fix this first: %s"
                    (ex.ToString())
            )
            failwith "unreachable"

        (serverWallet :> IDisposable).Dispose()
    }


    [<Test>]
    member __.``can open channel with LND and send htlcs``() = Helpers.RunAsyncTest <| async {
        let! channelId, clientWallet, bitcoind, electrumServer, lnd, fundingAmount = OpenChannelWithFundee None

        do! SendHtlcPaymentsToLnd clientWallet lnd channelId fundingAmount

        TearDown clientWallet bitcoind electrumServer lnd
    }

    [<Test>]
    member __.``can open channel with LND and send invalid htlc``() = Helpers.RunAsyncTest <| async {
        let! channelId, clientWallet, bitcoind, electrumServer, lnd, fundingAmount = OpenChannelWithFundee None

        let channelInfoBeforeAnyPayment = clientWallet.ChannelStore.ChannelInfo channelId
        match channelInfoBeforeAnyPayment.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        let! sendHtlcPayment1Res =
            async {
                let transferAmount =
                    let accountBalance = Money(channelInfoBeforeAnyPayment.SpendableBalance, MoneyUnit.BTC)
                    TransferAmount (walletToWalletTestPayment1Amount.ToDecimal MoneyUnit.BTC, accountBalance.ToDecimal MoneyUnit.BTC, Currency.BTC)
                let! invoiceOpt =
                    lnd.CreateInvoice transferAmount (TimeSpan.FromSeconds 1. |> Some)
                let invoice = UnwrapOption invoiceOpt "Failed to create first invoice"

                do! Async.Sleep 2000

                return!
                    Lightning.Network.SendHtlcPayment
                        clientWallet.NodeClient
                        channelId
                        (PaymentInvoice.Parse invoice.BOLT11)
                        true
            }
        match sendHtlcPayment1Res with
        | Error _err ->
            let channelInfoAfterPayment1 = clientWallet.ChannelStore.ChannelInfo channelId
            match channelInfoAfterPayment1.Status with
            | ChannelStatus.Active -> ()
            | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

            let! lndBalanceAfterPayment1 = lnd.ChannelBalance()

            if Money(channelInfoAfterPayment1.Balance, MoneyUnit.BTC) <> fundingAmount then
                return failwith "incorrect balance after failed payment 1"
            if lndBalanceAfterPayment1 <> Money.Zero then
                return failwith "incorrect lnd balance after failed payment 1"
        | Ok _ ->
            return failwith "SendHtlcPayment returtned ok"
        TearDown clientWallet bitcoind electrumServer lnd
    }

    [<Category "HtlcOnChainEnforce">]
    [<Test>]
    member __.``can open channel with LND and send invalid htlc but settle on-chain (force close initiated by lnd)``() = Helpers.RunAsyncTest <| async {
        let! channelId, clientWallet, bitcoind, electrumServer, lnd, _fundingAmount = OpenChannelWithFundee None
        Console.WriteLine("*** line " + __LINE__)
        let channelInfo = clientWallet.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)
        Console.WriteLine("*** line " + __LINE__)
        let! _sendHtlcPayment1Res =
            async {
                let transferAmount =
                    let accountBalance = Money(channelInfo.SpendableBalance, MoneyUnit.BTC)
                    TransferAmount (walletToWalletTestPayment1Amount.ToDecimal MoneyUnit.BTC, accountBalance.ToDecimal MoneyUnit.BTC, Currency.BTC)
                let! invoiceOpt =
                    lnd.CreateInvoice transferAmount (TimeSpan.FromSeconds 1. |> Some)
                let invoice = UnwrapOption invoiceOpt "Failed to create first invoice"

                do! Async.Sleep 2000

                return!
                    Lightning.Network.SendHtlcPayment
                        clientWallet.NodeClient
                        channelId
                        (PaymentInvoice.Parse invoice.BOLT11)
                        false
            }
        Console.WriteLine("*** line " + __LINE__)
        let fundingOutPoint =
            let fundingTxId = uint256(channelInfo.FundingTxId.ToString())
            let fundingOutPointIndex = channelInfo.FundingOutPointIndex
            OutPoint(fundingTxId, fundingOutPointIndex)
        Console.WriteLine("*** line " + __LINE__)
        // We use `Async.Start` because close channel api doesn't return until close/sweep process is finished
        lnd.CloseChannel fundingOutPoint true |> Async.Start
        Console.WriteLine("*** line " + __LINE__)
        do! Async.Sleep 5000

        // wait for force-close transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500
        Console.WriteLine("*** line " + __LINE__)
        let! balanceBeforeFundsReclaimed = clientWallet.GetBalance()
        Console.WriteLine("*** line " + __LINE__)
        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

        let rec waitForClosingTx () =
            async {
                let! result = ChainWatcher.CheckForChannelForceCloseAndSaveUnresolvedHtlcs channelId clientWallet.ChannelStore
                if result then
                    return ()
                else
                    do! Async.Sleep 500
                    return! waitForClosingTx()
            }
        Console.WriteLine("*** line " + __LINE__)
        do! waitForClosingTx ()
        Console.WriteLine("*** line " + __LINE__)
        let rec waitUntilReadyForBroadcastIsNotEmpty () =
            async {
                let! readyForBroadcast = ChainWatcher.CheckForChannelReadyToBroadcastHtlcTransactions channelId clientWallet.ChannelStore
                if readyForBroadcast.IsEmpty () then
                    bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)
                    do! Async.Sleep 100
                    return! waitUntilReadyForBroadcastIsNotEmpty ()
                else
                    return readyForBroadcast
            }

        let! readyToBroadcastHtlcTxs = waitUntilReadyForBroadcastIsNotEmpty()
        Console.WriteLine("*** line " + __LINE__)
        let rec broadcastUntilListIsEmpty (readyToBroadcastList: HtlcTxsList) (feeSum: Money) =
            async {
                if readyToBroadcastList.IsEmpty() then
                    return feeSum
                else
                    let! htlcTx, rest = (Lightning.Node.Client clientWallet.NodeClient).CreateHtlcTxForHtlcTxsList readyToBroadcastHtlcTxs clientWallet.Password
                    do! ChannelManager.BroadcastHtlcTxAndAddToWatchList htlcTx clientWallet.ChannelStore |> Async.Ignore

                    bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

                    return! broadcastUntilListIsEmpty rest (feeSum + (Money.Satoshis htlcTx.Fee.EstimatedFeeInSatoshis))
            }

        let! feesPaid = broadcastUntilListIsEmpty readyToBroadcastHtlcTxs Money.Zero
        Console.WriteLine("*** line " + __LINE__)
        do! clientWallet.WaitForBalance (balanceBeforeFundsReclaimed + walletToWalletTestPayment1Amount - feesPaid) |> Async.Ignore
        Console.WriteLine("*** line " + __LINE__)
        TearDown clientWallet bitcoind electrumServer lnd
        Console.WriteLine("*** line " + __LINE__)
    }

    [<Test>]
    member __.``can accept channel from LND and receive htlcs``() = Helpers.RunAsyncTest <| async {
        let! channelId, serverWallet, bitcoind, electrumServer, lnd = AcceptChannelFromLndFunder ()

        let channelInfoBeforeAnyPayment = serverWallet.ChannelStore.ChannelInfo channelId
        match channelInfoBeforeAnyPayment.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        let balanceBeforeAnyPayment = Money(channelInfoBeforeAnyPayment.Balance, MoneyUnit.BTC)

        let invoiceManager = InvoiceManagement (serverWallet.Account :?> NormalUtxoAccount, serverWallet.Password)

        let sendLndPayment1Job = async {
            Console.WriteLine("*** line " + __LINE__)
            // Wait for lnd to recognize we're online
            do! Async.Sleep 10000
            Console.WriteLine("*** line " + __LINE__)

            let amountInSatoshis =
                Convert.ToUInt64 walletToWalletTestPayment1Amount.Satoshi
            let invoice1InString = invoiceManager.CreateInvoice amountInSatoshis "Payment 1"

            Console.WriteLine("*** line " + __LINE__)
            do! lnd.SendPayment invoice1InString
            Console.WriteLine("*** line " + __LINE__)
        }
        let receiveGeewalletPayment = async {
            Console.WriteLine("*** line " + __LINE__)
            let! receiveHtlcPaymentRes =
                Lightning.Network.ReceiveLightningEvent serverWallet.NodeServer channelId true
            Console.WriteLine("*** line " + __LINE__)
            return UnwrapResult receiveHtlcPaymentRes "ReceiveHtlcPayment failed"
        }

        let! (_, receiveLightningEventResult) = AsyncExtensions.MixedParallel2 sendLndPayment1Job receiveGeewalletPayment

        match receiveLightningEventResult with
        | IncomingChannelEvent.HtlcPayment status ->
            Assert.AreNotEqual (HtlcSettleStatus.Fail, status, "htlc payment failed gracefully")
            Assert.AreNotEqual (HtlcSettleStatus.NotSettled, status, "htlc payment didn't get settled which shouldn't happen because ReceiveLightningEvent's settleHTLCImmediately should be true")

            let channelInfoAfterPayment1 = serverWallet.ChannelStore.ChannelInfo channelId
            match channelInfoAfterPayment1.Status with
            | ChannelStatus.Active -> ()
            | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

            if Money(channelInfoAfterPayment1.Balance, MoneyUnit.BTC) <> balanceBeforeAnyPayment + walletToWalletTestPayment1Amount then
                return failwith "incorrect balance after receiving payment 1"
        | _ ->
            Assert.Fail "received non-htlc lightning event"

        TearDown serverWallet bitcoind electrumServer lnd
    }

    [<Category "HtlcOnChainEnforce">]
    [<Test>]
    member __.``can accept channel from LND and receive htlcs but settle on-chain (force close initiated by lnd)``() = Helpers.RunAsyncTest <| async {
        Console.WriteLine("*** line " + __LINE__)
        let! channelId, serverWallet, bitcoind, electrumServer, lnd = AcceptChannelFromLndFunder ()
        Console.WriteLine("*** line " + __LINE__)
        let channelInfoBeforeAnyPayment = serverWallet.ChannelStore.ChannelInfo channelId
        match channelInfoBeforeAnyPayment.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        let invoiceManager = InvoiceManagement (serverWallet.Account :?> NormalUtxoAccount, serverWallet.Password)
        Console.WriteLine("*** line " + __LINE__)
        let sendLndPayment1Job = async {
            Console.WriteLine("*** line " + __LINE__)
            // Wait for lnd to recognize we're online
            do! Async.Sleep 10000
            Console.WriteLine("*** line " + __LINE__)

            let amountInSatoshis =
                Convert.ToUInt64 walletToWalletTestPayment1Amount.Satoshi
            let invoice1InString = invoiceManager.CreateInvoice amountInSatoshis "Payment 1"
            Console.WriteLine("*** line " + __LINE__)

            // We use `Async.Start` because send payment api doesn't return until payment is settled (which doesn't happen immediately in this test)
            lnd.SendPayment invoice1InString |> Async.Start
        }
        let receiveGeewalletPayment = async {
            Console.WriteLine("*** line " + __LINE__)
            let! receiveHtlcPaymentRes =
                Lightning.Network.ReceiveLightningEvent serverWallet.NodeServer channelId false
            Console.WriteLine("*** line " + __LINE__)
            return UnwrapResult receiveHtlcPaymentRes "ReceiveHtlcPayment failed"
        }

        let! (_, receiveLightningEventResult) = AsyncExtensions.MixedParallel2 sendLndPayment1Job receiveGeewalletPayment
        Console.WriteLine("*** line " + __LINE__)
        match receiveLightningEventResult with
        | IncomingChannelEvent.HtlcPayment status ->
            Assert.AreEqual (HtlcSettleStatus.NotSettled, status, "htlc payment got settled")

            let channelInfoAfterPayment1 = serverWallet.ChannelStore.ChannelInfo channelId
            match channelInfoAfterPayment1.Status with
            | ChannelStatus.Active -> ()
            | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

            let fundingOutPoint =
                let fundingTxId = uint256(channelInfoAfterPayment1.FundingTxId.ToString())
                let fundingOutPointIndex = channelInfoAfterPayment1.FundingOutPointIndex
                OutPoint(fundingTxId, fundingOutPointIndex)
            Console.WriteLine("*** line " + __LINE__)
            // We use `Async.Start` because close channel api doesn't return until close/sweep process is finished
            lnd.CloseChannel fundingOutPoint true |> Async.Start
            Console.WriteLine("*** line " + __LINE__)
            do! Async.Sleep 5000
            
            // wait for force-close transaction to appear in mempool
            while bitcoind.GetTxIdsInMempool().Length = 0 do
                do! Async.Sleep 500
            Console.WriteLine("*** line " + __LINE__)
            let! balanceBeforeFundsReclaimed = serverWallet.GetBalance()
            Console.WriteLine("*** line " + __LINE__)
            bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)
            Console.WriteLine("*** line " + __LINE__)
            let rec waitForClosingTx () =
                async {
                    let! result = ChainWatcher.CheckForChannelForceCloseAndSaveUnresolvedHtlcs channelId serverWallet.ChannelStore
                    if result then
                        return ()
                    else
                        do! Async.Sleep 500
                        return! waitForClosingTx()
                }

            do! waitForClosingTx ()
            Console.WriteLine("*** line " + __LINE__)
            let rec waitUntilReadyForBroadcastIsNotEmpty () =
                async {
                    let! readyForBroadcast = ChainWatcher.CheckForChannelReadyToBroadcastHtlcTransactions channelId serverWallet.ChannelStore
                    if readyForBroadcast.IsEmpty () then
                        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)
                        do! Async.Sleep 100
                        return! waitUntilReadyForBroadcastIsNotEmpty ()
                    else
                        return readyForBroadcast
                }

            let! readyToBroadcastHtlcTxs = waitUntilReadyForBroadcastIsNotEmpty()
            Console.WriteLine("*** line " + __LINE__)
            let rec broadcastUntilListIsEmpty (readyToBroadcastList: HtlcTxsList) (feeSum: Money) =
                async {
                    if readyToBroadcastList.IsEmpty() then
                        return feeSum
                    else
                        let! htlcTx, rest = (Lightning.Node.Server serverWallet.NodeServer).CreateHtlcTxForHtlcTxsList readyToBroadcastHtlcTxs serverWallet.Password
                        do! ChannelManager.BroadcastHtlcTxAndAddToWatchList htlcTx serverWallet.ChannelStore |> Async.Ignore
                        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

                        return! broadcastUntilListIsEmpty rest (feeSum + (Money.Satoshis htlcTx.Fee.EstimatedFeeInSatoshis))
                }

            let! feesPaid = broadcastUntilListIsEmpty readyToBroadcastHtlcTxs Money.Zero
            Console.WriteLine("*** line " + __LINE__)
            do! serverWallet.WaitForBalance (balanceBeforeFundsReclaimed + walletToWalletTestPayment1Amount - feesPaid) |> Async.Ignore
            Console.WriteLine("*** line " + __LINE__)
        | _ ->
            Assert.Fail "received non-htlc lightning event"
        Console.WriteLine("*** line " + __LINE__)
        TearDown serverWallet bitcoind electrumServer lnd
    }
    [<Category "HtlcOnChainEnforce">]
    [<Test>]
    member __.``can accept channel from LND and receive htlcs but settle on-chain (force close initiated by geewallet)``() = Helpers.RunAsyncTest <| async {
        let! channelId, serverWallet, bitcoind, electrumServer, lnd = AcceptChannelFromLndFunder ()
        Console.WriteLine("*** line " + __LINE__)
        do! serverWallet.FundByMining bitcoind lnd
        Console.WriteLine("*** line " + __LINE__)
        let channelInfoBeforeAnyPayment = serverWallet.ChannelStore.ChannelInfo channelId
        match channelInfoBeforeAnyPayment.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)
        Console.WriteLine("*** line " + __LINE__)
        let invoiceManager = InvoiceManagement (serverWallet.Account :?> NormalUtxoAccount, serverWallet.Password)
        Console.WriteLine("*** line " + __LINE__)
        let sendLndPayment1Job = async {
            Console.WriteLine("*** line " + __LINE__)
            // Wait for lnd to recognize we're online
            do! Async.Sleep 10000
            Console.WriteLine("*** line " + __LINE__)

            let amountInSatoshis =
                Convert.ToUInt64 walletToWalletTestPayment1Amount.Satoshi
            let invoice1InString = invoiceManager.CreateInvoice amountInSatoshis "Payment 1"
            Console.WriteLine("*** line " + __LINE__)

            // We use `Async.Start` because send payment api doesn't return until payment is settled (which doesn't happen immediately in this test)
            lnd.SendPayment invoice1InString |> Async.Start
        }
        let receiveGeewalletPayment = async {
            Console.WriteLine("*** line " + __LINE__)
            let! receiveHtlcPaymentRes =
                Lightning.Network.ReceiveLightningEvent serverWallet.NodeServer channelId false
            Console.WriteLine("*** line " + __LINE__)
            return UnwrapResult receiveHtlcPaymentRes "ReceiveHtlcPayment failed"
        }

        let! (_, receiveLightningEventResult) = AsyncExtensions.MixedParallel2 sendLndPayment1Job receiveGeewalletPayment
        Console.WriteLine("*** line " + __LINE__)
        match receiveLightningEventResult with
        | IncomingChannelEvent.HtlcPayment status ->
            Assert.AreEqual (HtlcSettleStatus.NotSettled, status, "htlc payment got settled")

            let channelInfoAfterPayment1 = serverWallet.ChannelStore.ChannelInfo channelId
            match channelInfoAfterPayment1.Status with
            | ChannelStatus.Active -> ()
            | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)
            Console.WriteLine("*** line " + __LINE__)
            let! _forceCloseTxId = (Lightning.Node.Server serverWallet.NodeServer).ForceCloseChannel channelId

            let locallyForceClosedData =
                match (serverWallet.ChannelStore.ChannelInfo channelId).Status with
                | ChannelStatus.LocallyForceClosed locallyForceClosedData ->
                    locallyForceClosedData
                | status -> failwith (SPrintF1 "unexpected channel status. Expected LocallyForceClosed, got %A" status)
            Console.WriteLine("*** line " + __LINE__)
            // wait for force-close transaction to appear in mempool
            while bitcoind.GetTxIdsInMempool().Length = 0 do
                do! Async.Sleep 500

            // Mine the force-close tx into a block
            bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)
            Console.WriteLine("*** line " + __LINE__)
            let! balanceBeforeFundsReclaimed = serverWallet.GetBalance()
            Console.WriteLine("*** line " + __LINE__)
            let rec waitForClosingTx () =
                async {
                    Console.WriteLine "Looking for closing tx"
                    let! result = ChainWatcher.CheckForChannelForceCloseAndSaveUnresolvedHtlcs channelId serverWallet.ChannelStore
                    if result then
                        return ()
                    else
                        do! Async.Sleep 500
                        return! waitForClosingTx()
                }

            do! waitForClosingTx ()
            Console.WriteLine("*** line " + __LINE__)
            let rec waitUntilReadyForBroadcastIsNotEmpty () =
                async {
                    let! readyForBroadcast = ChainWatcher.CheckForChannelReadyToBroadcastHtlcTransactions channelId serverWallet.ChannelStore
                    if readyForBroadcast.IsEmpty () then
                        Console.WriteLine "No ready for broadcast, rechecking"
                        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)
                        do! Async.Sleep 100
                        return! waitUntilReadyForBroadcastIsNotEmpty ()
                    else
                        return readyForBroadcast
                }

            let! readyToBroadcastHtlcTxs = waitUntilReadyForBroadcastIsNotEmpty()
            Console.WriteLine("*** line " + __LINE__)
            let rec broadcastUntilListIsEmpty (readyToBroadcastList: HtlcTxsList) (feeSum: Money) =
                async {
                    if readyToBroadcastList.IsEmpty() then
                        return feeSum
                    else
                        let! htlcTx, rest = (Lightning.Node.Server serverWallet.NodeServer).CreateHtlcTxForHtlcTxsList readyToBroadcastHtlcTxs serverWallet.Password
                        Console.WriteLine (sprintf "Broadcasting... %s" (htlcTx.Tx.ToString()))

                        do! ChannelManager.BroadcastHtlcTxAndAddToWatchList htlcTx serverWallet.ChannelStore |> Async.Ignore
                        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

                        return! broadcastUntilListIsEmpty rest (feeSum + (Money.Satoshis htlcTx.Fee.EstimatedFeeInSatoshis))
                }

            let! feesPaidFor2ndStageHtlcTx = broadcastUntilListIsEmpty readyToBroadcastHtlcTxs Money.Zero

            bitcoind.GenerateBlocksToDummyAddress (locallyForceClosedData.ToSelfDelay |> uint32 |> BlockHeightOffset32)
            Console.WriteLine("*** line " + __LINE__)
            let rec checkForReadyToSpend2ndStageClaim ()  =
                async {
                    let! readyForBroadcast = ChainWatcher.CheckForReadyToSpendDelayedHtlcTransactions channelId serverWallet.ChannelStore
                    if List.isEmpty readyForBroadcast then
                        Console.WriteLine "No ready for spend, rechecking"
                        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)
                        do! Async.Sleep 100
                        return! checkForReadyToSpend2ndStageClaim ()
                    else
                        return readyForBroadcast
                }

            let! readyToSpend2ndStages = checkForReadyToSpend2ndStageClaim()

            let rec spend2ndStages (readyToSpend2ndStages: List<AmountInSatoshis * TransactionIdentifier>)  =
                async {
                    let! recoveryTxs = (Lightning.Node.Server serverWallet.NodeServer).CreateRecoveryTxForDelayedHtlcTx channelId readyToSpend2ndStages
                    Console.WriteLine (sprintf "Broadcasting... %A" recoveryTxs)
                    let rec broadcastSpendingTxs (recoveryTxs: List<HtlcRecoveryTx>) (feeSum: Money) =
                        async {
                            match recoveryTxs with
                            | [] -> return feeSum
                            | recoveryTx::rest ->
                                do! ChannelManager.BroadcastHtlcRecoveryTxAndRemoveFromWatchList recoveryTx serverWallet.ChannelStore |> Async.Ignore
                                bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

                                return! broadcastSpendingTxs rest (feeSum + (Money.Satoshis recoveryTx.Fee.EstimatedFeeInSatoshis))
                        }
                    return! broadcastSpendingTxs recoveryTxs Money.Zero
                }
            Console.WriteLine("*** line " + __LINE__)
            let! feePaidForClaiming2ndtSage = spend2ndStages readyToSpend2ndStages
            Console.WriteLine("*** line " + __LINE__)
            do! serverWallet.WaitForBalance (balanceBeforeFundsReclaimed + walletToWalletTestPayment1Amount - feesPaidFor2ndStageHtlcTx - feePaidForClaiming2ndtSage) |> Async.Ignore

        | _ ->
            Assert.Fail "received non-htlc lightning event"

        TearDown serverWallet bitcoind electrumServer lnd
    }

    [<Test>]
    [<Category "HtlcOnChainEnforce">]
    member __.``can accept channel from LND and send invalid htlc but settle on-chain (force close initiated by geewallet)``() = Helpers.RunAsyncTest <| async {
        let! channelId, clientWallet, bitcoind, electrumServer, lnd, _fundingAmount = OpenChannelWithFundee None
        Console.WriteLine("*** line " + __LINE__)
        let channelInfo = clientWallet.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)
        Console.WriteLine("*** line " + __LINE__)
        let! _sendHtlcPayment1Res =
            async {
                let transferAmount =
                    let accountBalance = Money(channelInfo.SpendableBalance, MoneyUnit.BTC)
                    TransferAmount (walletToWalletTestPayment1Amount.ToDecimal MoneyUnit.BTC, accountBalance.ToDecimal MoneyUnit.BTC, Currency.BTC)
                let! invoiceOpt =
                    lnd.CreateInvoice transferAmount (TimeSpan.FromSeconds 1. |> Some)
                let invoice = UnwrapOption invoiceOpt "Failed to create first invoice"

                do! Async.Sleep 2000

                return!
                    Lightning.Network.SendHtlcPayment
                        clientWallet.NodeClient
                        channelId
                        (PaymentInvoice.Parse invoice.BOLT11)
                        false
            }
        Console.WriteLine("*** line " + __LINE__)
        let! _forceCloseTxId = (Lightning.Node.Client clientWallet.NodeClient).ForceCloseChannel channelId
        Console.WriteLine("*** line " + __LINE__)
        let locallyForceClosedData =
            match (clientWallet.ChannelStore.ChannelInfo channelId).Status with
            | ChannelStatus.LocallyForceClosed locallyForceClosedData ->
                locallyForceClosedData
            | status -> failwith (SPrintF1 "unexpected channel status. Expected LocallyForceClosed, got %A" status)
        Console.WriteLine("*** line " + __LINE__)
        // wait for force-close transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500

        Infrastructure.LogDebug (SPrintF1 "the time lock is %i blocks" locallyForceClosedData.ToSelfDelay)
        Console.WriteLine("*** line " + __LINE__)
        // wait for force-close transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500

        // Mine the force-close tx into a block
        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)
        Console.WriteLine("*** line " + __LINE__)
        let! balanceBeforeFundsReclaimed = clientWallet.GetBalance()
        Console.WriteLine("*** line " + __LINE__)
        let rec waitForClosingTx () =
            async {
                Console.WriteLine "Looking for closing tx"
                let! result = ChainWatcher.CheckForChannelForceCloseAndSaveUnresolvedHtlcs channelId clientWallet.ChannelStore
                if result then
                    return ()
                else
                    do! Async.Sleep 500
                    return! waitForClosingTx()
            }

        do! waitForClosingTx ()
        Console.WriteLine("*** line " + __LINE__)
        let rec waitUntilReadyForBroadcastIsNotEmpty () =
            async {
                let! readyForBroadcast = ChainWatcher.CheckForChannelReadyToBroadcastHtlcTransactions channelId clientWallet.ChannelStore
                if readyForBroadcast.IsEmpty () then
                    Console.WriteLine "No ready for broadcast, rechecking"
                    bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)
                    do! Async.Sleep 100
                    return! waitUntilReadyForBroadcastIsNotEmpty ()
                else
                    return readyForBroadcast
            }

        let! readyToBroadcastHtlcTxs = waitUntilReadyForBroadcastIsNotEmpty()
        Console.WriteLine("*** line " + __LINE__)
        let rec broadcastUntilListIsEmpty (readyToBroadcastList: HtlcTxsList) (feeSum: Money) =
            async {
                if readyToBroadcastList.IsEmpty() then
                    return feeSum
                else
                    let! htlcTx, rest = (Lightning.Node.Client clientWallet.NodeClient).CreateHtlcTxForHtlcTxsList readyToBroadcastHtlcTxs clientWallet.Password
                    Console.WriteLine (sprintf "Broadcasting... %s" (htlcTx.Tx.ToString()))

                    do! ChannelManager.BroadcastHtlcTxAndAddToWatchList htlcTx clientWallet.ChannelStore |> Async.Ignore
                    bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

                    return! broadcastUntilListIsEmpty rest (feeSum + (Money.Satoshis htlcTx.Fee.EstimatedFeeInSatoshis))
            }
        
        let! feesPaidFor2ndStageHtlcTx = broadcastUntilListIsEmpty readyToBroadcastHtlcTxs Money.Zero

        bitcoind.GenerateBlocksToDummyAddress (locallyForceClosedData.ToSelfDelay |> uint32 |> BlockHeightOffset32)
        Console.WriteLine("*** line " + __LINE__)
        let rec checkForReadyToSpend2ndStageClaim ()  =
            async {
                let! readyForBroadcast = ChainWatcher.CheckForReadyToSpendDelayedHtlcTransactions channelId clientWallet.ChannelStore
                if List.isEmpty readyForBroadcast then
                    Console.WriteLine "No ready for spend, rechecking"
                    bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)
                    do! Async.Sleep 100
                    return! checkForReadyToSpend2ndStageClaim ()
                else
                    return readyForBroadcast
            }

        let! readyToSpend2ndStages = checkForReadyToSpend2ndStageClaim()
        Console.WriteLine("*** line " + __LINE__)
        let rec spend2ndStages (readyToSpend2ndStages: List<AmountInSatoshis * TransactionIdentifier>)  =
            async {
                let! recoveryTxs = (Lightning.Node.Client clientWallet.NodeClient).CreateRecoveryTxForDelayedHtlcTx channelId readyToSpend2ndStages
                Console.WriteLine (sprintf "Broadcasting... %A" recoveryTxs)
                let rec broadcastSpendingTxs (recoveryTxs: List<HtlcRecoveryTx>) (feeSum: Money) =
                    async {
                        match recoveryTxs with
                        | [] -> return feeSum
                        | recoveryTx::rest ->
                            do! ChannelManager.BroadcastHtlcRecoveryTxAndRemoveFromWatchList recoveryTx clientWallet.ChannelStore |> Async.Ignore
                            bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

                            return! broadcastSpendingTxs rest (feeSum + (Money.Satoshis recoveryTx.Fee.EstimatedFeeInSatoshis))
                    }
                return! broadcastSpendingTxs recoveryTxs Money.Zero
            }

        let! feePaidForClaiming2ndtSage = spend2ndStages readyToSpend2ndStages
        Console.WriteLine("*** line " + __LINE__)
        do! clientWallet.WaitForBalance (balanceBeforeFundsReclaimed + walletToWalletTestPayment1Amount - feesPaidFor2ndStageHtlcTx - feePaidForClaiming2ndtSage) |> Async.Ignore

        TearDown clientWallet bitcoind electrumServer lnd
    }

    [<Test>]
    member __.``can close channel with LND``() = Helpers.RunAsyncTest <| async {

        let! channelId, clientWallet, bitcoind, electrumServer, lnd, _fundingAmount =
            try
                OpenChannelWithFundee None
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: channel-closing inconclusive because Channel open failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"

        let! closeChannelRes = Lightning.Network.CloseChannel clientWallet.NodeClient channelId
        match closeChannelRes with
        | Ok _ -> ()
        | Error err -> return failwith (SPrintF1 "error when closing channel: %s" (err :> IErrorMsg).Message)

        match (clientWallet.ChannelStore.ChannelInfo channelId).Status with
        | ChannelStatus.Closing -> ()
        | status -> return failwith (SPrintF1 "unexpected channel status. Expected Closing, got %A" status)

        // Mine 10 blocks to make sure closing tx is confirmed
        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 (uint32 10))

        let rec waitForClosingTxConfirmed attempt = async {
            Infrastructure.LogDebug (SPrintF1 "Checking if closing tx is finished, attempt #%d" attempt)
            if attempt = 10 then
                return Error "Closing tx not confirmed after maximum attempts"
            else
                let! closingTxResult = Lightning.Network.CheckClosingFinished clientWallet.ChannelStore channelId
                match closingTxResult with
                | Tx (Full, _closingTx) ->
                    return Ok ()
                | _ ->
                    do! Async.Sleep 1000
                    return! waitForClosingTxConfirmed (attempt + 1)
        }

        let! closingTxConfirmedRes = waitForClosingTxConfirmed 0
        match closingTxConfirmedRes with
        | Ok _ -> ()
        | Error err -> return failwith (SPrintF1 "error when waiting for closing tx to confirm: %s" err)

        TearDown clientWallet bitcoind electrumServer lnd
    }

    [<Test>]
    member __.``can accept channel from LND``() = Helpers.RunAsyncTest <| async {
        let! _channelId, serverWallet, bitcoind, electrumServer, lnd = AcceptChannelFromLndFunder ()

        TearDown serverWallet bitcoind electrumServer lnd
    }

    [<Test>]
    member __.``can accept channel closure from LND``() = Helpers.RunAsyncTest <| async {
        let! channelId, serverWallet, bitcoind, electrumServer, lnd =
            try
                AcceptChannelFromLndFunder ()
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: channel-closing inconclusive because Channel accept failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"

        let channelInfo = serverWallet.ChannelStore.ChannelInfo channelId

        // wait for lnd to realise we're offline
        do! Async.Sleep 1000
        let fundingOutPoint =
            let fundingTxId = uint256(channelInfo.FundingTxId.ToString())
            let fundingOutPointIndex = channelInfo.FundingOutPointIndex
            OutPoint(fundingTxId, fundingOutPointIndex)
        let closeChannelTask = async {
            match serverWallet.NodeEndPoint with
            | EndPointType.Tcp endPoint ->
                Console.WriteLine("*** line " + __LINE__)
                do! lnd.ConnectTo endPoint
                Console.WriteLine("*** line " + __LINE__)
                do! Async.Sleep 1000
                Console.WriteLine("*** line " + __LINE__)
                do! lnd.CloseChannel fundingOutPoint false
                Console.WriteLine("*** line " + __LINE__)
                return ()
            | EndPointType.Tor _torEndPoint ->
                failwith "this should be a nonexistent case as all LND tests are done using TCP at the moment and TCP connections will always have a NodeEndPoint"
        }
        let awaitCloseTask = async {
            let rec receiveEvent () = async {
                Console.WriteLine("*** line " + __LINE__)
                let! receivedEvent = Lightning.Network.ReceiveLightningEvent serverWallet.NodeServer channelId true
                Console.WriteLine("*** line " + __LINE__)
                match receivedEvent with
                | Error err ->
                    return Error (SPrintF1 "Failed to receive shutdown msg from LND: %A" err)
                | Ok event when event = IncomingChannelEvent.Shutdown ->
                    return Ok ()
                | _ -> return! receiveEvent ()
            }

            let! receiveEventRes = receiveEvent()
            UnwrapResult receiveEventRes "failed to accept close channel"

            // Wait for the closing transaction to appear in mempool
            while bitcoind.GetTxIdsInMempool().Length = 0 do
                Thread.Sleep 500

            // Mine blocks on top of the closing transaction to make it confirmed.
            let minimumDepth = BlockHeightOffset32 6u
            bitcoind.GenerateBlocksToDummyAddress minimumDepth
            return ()
        }

        let! (), () = AsyncExtensions.MixedParallel2 closeChannelTask awaitCloseTask

        TearDown serverWallet bitcoind electrumServer lnd

        return ()
    }

    [<Test>]
    member __.``can force-close channel with lnd``() = Helpers.RunAsyncTest <| async {
        let! channelId, clientWallet, bitcoind, electrumServer, lnd, _fundingAmount =
            try
                OpenChannelWithFundee None
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: channel-closing inconclusive because Channel open failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"

        let! _forceCloseTxId = (Lightning.Node.Client clientWallet.NodeClient).ForceCloseChannel channelId

        let locallyForceClosedData =
            match (clientWallet.ChannelStore.ChannelInfo channelId).Status with
            | ChannelStatus.LocallyForceClosed locallyForceClosedData ->
                locallyForceClosedData
            | status -> failwith (SPrintF1 "unexpected channel status. Expected LocallyForceClosed, got %A" status)

        // wait for force-close transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500

        Infrastructure.LogDebug (SPrintF1 "the time lock is %i blocks" locallyForceClosedData.ToSelfDelay)

        let! balanceBeforeFundsReclaimed = clientWallet.GetBalance()

        // Mine the force-close tx into a block
        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

        // Mine blocks to release time-lock
        bitcoind.GenerateBlocksToDummyAddress
            (BlockHeightOffset32 (uint32 locallyForceClosedData.ToSelfDelay))

        let! spendingTxResult =
            let commitmentTx = clientWallet.ChannelStore.GetCommitmentTx channelId
            (Lightning.Node.Client clientWallet.NodeClient).CreateRecoveryTxForForceClose
                channelId
                commitmentTx

        let recoveryTx = UnwrapResult spendingTxResult "Local output is dust, recovery tx cannot be created"

        let! _recoveryTxId =
            ChannelManager.BroadcastRecoveryTxAndCloseChannel recoveryTx clientWallet.ChannelStore

        // wait for spending transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500

        // Mine the spending tx into a block
        bitcoind.GenerateBlocksToDummyAddress (BlockHeightOffset32 1u)

        Infrastructure.LogDebug "waiting for our wallet balance to increase"
        let! _balanceAfterFundsReclaimed =
            let amount = balanceBeforeFundsReclaimed + Money(1.0m, MoneyUnit.Satoshi)
            clientWallet.WaitForBalance amount

        TearDown clientWallet bitcoind electrumServer lnd
    }


    [<Category "G2G_UpdateFeeMsg_Funder">]
    [<Test>]
    member __.``can update fee after sending htlc payments (funder)``() = Helpers.RunAsyncTest <| async {
        let! channelId, clientWallet, bitcoind, electrumServer, lnd, _fundingAmount =
            try
                OpenChannelWithFundee (Some Config.FundeeNodeEndpoint)
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: UpdateFee message support inconclusive because Channel open failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"
        let! feeRate = ElectrumServer.EstimateFeeRate()

        try
            let! sendPaymentRes = SendHtlcPaymentToGW channelId clientWallet "invoice-1.txt"
            UnwrapResult sendPaymentRes "sending htlc failed."
        with
        | ex ->
            Assert.Fail (
                sprintf
                    "Inconclusive: UpdateFee message support inconclusive because sending of htlc payments failed, fix this first: %s"
                    (ex.ToString())
            )
            failwith "unreachable"

        ElectrumServer.SetEstimatedFeeRate (feeRate * 4u)
        let! newFeeRateOpt = clientWallet.ChannelStore.FeeUpdateRequired channelId
        let newFeeRate = UnwrapOption newFeeRateOpt "Fee update should be required"
        let! updateFeeRes =
            (Node.Client clientWallet.NodeClient).UpdateFee channelId newFeeRate
        UnwrapResult updateFeeRes "UpdateFee failed"

        try
            let! sendPaymentRes = SendHtlcPaymentToGW channelId clientWallet "invoice-2.txt"
            UnwrapResult sendPaymentRes "sending htlc failed."
        with
        | ex ->
            Assert.Fail (
                sprintf
                    "Inconclusive: sending of htlc payments failed after UpdateFee message handling: %s"
                    (ex.ToString())
            )
            failwith "unreachable"

        do! ClientCloseChannel clientWallet bitcoind channelId

        TearDown clientWallet bitcoind electrumServer lnd
    }

    [<Category "G2G_UpdateFeeMsg_Fundee">]
    [<Test>]
    member __.``can accept fee update after receiving htlc payments, with geewallet (fundee)``() = Helpers.RunAsyncTest <| async {
        let! serverWallet, channelId =
            try
                AcceptChannelFromGeewalletFunder ()
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: UpdateFee message support inconclusive because Channel accept failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"

        let! feeRate = ElectrumServer.EstimateFeeRate()

        try
            do! ReceiveHtlcPaymentToGW channelId serverWallet "invoice-1.txt" |> Async.Ignore
        with
        | ex ->
            Assert.Fail (
                sprintf
                    "Inconclusive: UpdateFee message support inconclusive because receiving of htlc payments failed, fix this first: %s"
                    (ex.ToString())
            )
            failwith "unreachable"

        ElectrumServer.SetEstimatedFeeRate (feeRate * 4u)
        let! acceptUpdateFeeRes =
            Lightning.Network.AcceptUpdateFee serverWallet.NodeServer channelId
        UnwrapResult acceptUpdateFeeRes "AcceptUpdateFee failed"

        try
            do! ReceiveHtlcPaymentToGW channelId serverWallet "invoice-2.txt" |> Async.Ignore
        with
        | ex ->
            Assert.Fail (
                sprintf
                    "Inconclusive: receiving of htlc payments failed after UpdateFee message handling: %s"
                    (ex.ToString())
            )
            failwith "unreachable"

        let! closeChannelRes = Lightning.Network.AcceptCloseChannel serverWallet.NodeServer channelId
        match closeChannelRes with
        | Ok _ -> ()
        | Error err -> return failwith (SPrintF1 "failed to accept close channel: %A" err)

        (serverWallet :> IDisposable).Dispose()
    }


    [<Category "G2G_MutualCloseCpfp_Funder">]
    [<Test>]
    member __.``can CPFP on mutual close (funder)``() = Helpers.RunAsyncTest <| async {
        let! channelId, clientWallet, bitcoind, electrumServer, lnd, _fundingAmount =
            try
                OpenChannelWithFundee (Some Config.FundeeNodeEndpoint)
            with
            | ex ->
                Assert.Fail (
                    sprintf
                        "Inconclusive: channel-closing inconclusive because Channel open failed, fix this first: %s"
                        (ex.ToString())
                )
                failwith "unreachable"

        try
            let! sendPaymentRes = SendHtlcPaymentToGW channelId clientWallet "invoice.txt"
            UnwrapResult sendPaymentRes "sending htlc failed."
        with
        | ex ->
            Assert.Fail (
                sprintf
                    "Inconclusive: channel-closing inconclusive because sending of htlc payments failed, fix this first: %s"
                    (ex.ToString())
            )
            failwith "unreachable"

        do! ClientCloseChannel clientWallet bitcoind channelId

        TearDown clientWallet bitcoind electrumServer lnd
    }


    [<Category "G2G_MutualCloseCpfp_Fundee">]
    [<Test>]
    member __.``can CPFP on mutual close  (fundee)``() = Helpers.RunAsyncTest <| async {
        let! serverWallet, channelId = AcceptChannelFromGeewalletFunder ()

        do! ReceiveHtlcPaymentToGW channelId serverWallet "invoice.txt" |> Async.Ignore

        let! closeChannelRes = Lightning.Network.AcceptCloseChannel serverWallet.NodeServer channelId
        match closeChannelRes with
        | Ok _ -> ()
        | Error err -> return failwith (SPrintF1 "failed to accept close channel: %A" err)

        let! oldFeeRate = ElectrumServer.EstimateFeeRate()

        let newFeeRate = oldFeeRate * 5u
        ElectrumServer.SetEstimatedFeeRate newFeeRate

        let rec waitForClosingTxConfirmed attempt = async {
            Infrastructure.LogDebug (SPrintF1 "Checking if closing tx is finished, attempt #%d" attempt)
            if attempt = 10 then
                return Error "Closing tx not confirmed after maximum attempts"
            else
                let! closingTxResult = Lightning.Network.CheckClosingFinished serverWallet.ChannelStore channelId
                match closingTxResult with
                | Tx (WaitingForFirstConf, closingTx) ->
                    let! cpfpCreationRes = ChannelManager.CreateCpfpTxOnMutualClose serverWallet.ChannelStore channelId closingTx serverWallet.Password
                    match cpfpCreationRes with
                    | Ok mutualCpfp ->
                        return Ok (closingTx, mutualCpfp)
                    | _ -> return Error "CPFP tx creation failed"
                | Tx (Full, _) ->
                    Assert.Fail "Inconclusive: Closing tx got confirmed before we get a chance to create CPFP tx"
                    return Error "Closing tx got confirmed before we get a chance to create CPFP tx"
                | _ ->
                    do! Async.Sleep 1000
                    return! waitForClosingTxConfirmed (attempt + 1)
        }

        let! closingTxConfirmedRes = waitForClosingTxConfirmed 0
        match closingTxConfirmedRes with
        | Ok (mutualCloseTx, mutualCpfpTx) ->
            let closingTx = Transaction.Parse(mutualCloseTx.Tx.ToString(), Network.RegTest)
            let! closingTxFee = FeesHelper.GetFeeFromTransaction closingTx
            let closingTxFeeRate =
                FeeRatePerKw.FromFeeAndVSize(closingTxFee, uint64 (closingTx.GetVirtualSize()))
            assert FeesHelper.FeeRatesApproxEqual closingTxFeeRate oldFeeRate

            let cpfpTx = Transaction.Parse(mutualCpfpTx.Tx.ToString(), Network.RegTest)
            let! txFeeWithCpfp = FeesHelper.GetFeeFromTransaction cpfpTx
            let txFeeRateWithCpfp =
                FeeRatePerKw.FromFeeAndVSize(txFeeWithCpfp, uint64 (cpfpTx.GetVirtualSize()))
            assert (not <| FeesHelper.FeeRatesApproxEqual txFeeRateWithCpfp oldFeeRate)
            assert (not <| FeesHelper.FeeRatesApproxEqual txFeeRateWithCpfp newFeeRate)
            let combinedFeeRateWithCpfp =
                FeeRatePerKw.FromFeeAndVSize(
                    txFeeWithCpfp + closingTxFee,
                    uint64 (closingTx.GetVirtualSize() + cpfpTx.GetVirtualSize())
                )
            assert FeesHelper.FeeRatesApproxEqual combinedFeeRateWithCpfp newFeeRate

            let! _cpfpTxId = Account.BroadcastRawTransaction serverWallet.Account.Currency (mutualCpfpTx.Tx.ToString())

            return ()
        | Error err -> return failwith (SPrintF1 "error when waiting for closing tx to confirm: %s" err)

        (serverWallet :> IDisposable).Dispose()
    }


    [<SetUp>]
    member __.Init() =
        Console.WriteLine("*** Time: " +  System.DateTime.Now.ToShortTimeString())
        let delete (fileName) =
            let invoiceFileFromFundeePath =
                Path.Combine (Path.GetTempPath(), fileName)
            // Clear the invoice file from previous runs
            File.Delete (invoiceFileFromFundeePath)
        delete "invoice.txt"
        delete "invoice-1.txt"
        delete "invoice-2.txt"