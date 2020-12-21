namespace GWallet.Backend.Tests.EndToEnd

open System.Threading // For AutoResetEvent and CancellationToken

open BTCPayServer.Lightning
open NBitcoin // For ExtKey
open DotNetLightning.Utils

open GWallet.Backend
open GWallet.Backend.UtxoCoin // For NormalUtxoAccount
open GWallet.Backend.UtxoCoin.Lightning
open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks
open GWallet.Regtest


module GwalletToLndChannelManagement =
    let OpenChannel(walletInstance: WalletInstance) (bitcoind: Bitcoind) (lnd : Lnd): Async<ChannelIdentifier> =
        async {
            do! walletInstance.FundByMining bitcoind lnd

            // fund geewallet
            let geewalletAccountAmount = Money (25m, MoneyUnit.BTC)
            let feeRate = FeeRatePerKw 2500u
            let! _txid = lnd.SendCoins geewalletAccountAmount walletInstance.Address feeRate

            // wait for lnd's transaction to appear in mempool
            while bitcoind.GetTxIdsInMempool().Length = 0 do
                do! Async.Sleep 500

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
            bitcoind.GenerateBlocks Config.MinimumDepth walletInstance.Address

            let! lndEndPoint = lnd.GetEndPoint()
            let! transferAmount = async {
                let amount = Money(0.002m, MoneyUnit.BTC)
                let! accountBalance = walletInstance.WaitForBalance amount
                return TransferAmount (amount.ToDecimal MoneyUnit.BTC, accountBalance.ToDecimal MoneyUnit.BTC, Currency.BTC)
            }
            let! metadata = ChannelManager.EstimateChannelOpeningFee (walletInstance.Account :?> NormalUtxoAccount) transferAmount
            let! pendingChannelRes =
                Lightning.Network.OpenChannel
                    walletInstance.Node
                    lndEndPoint
                    transferAmount
                    metadata
                    walletInstance.Password
            let pendingChannel = UnwrapResult pendingChannelRes "OpenChannel failed"
            let minimumDepth = (pendingChannel :> IChannelToBeOpened).ConfirmationsRequired
            let channelId = (pendingChannel :> IChannelToBeOpened).ChannelId
            let! fundingTxIdRes = pendingChannel.Accept()
            let _fundingTxId = UnwrapResult fundingTxIdRes "pendingChannel.Accept failed"
            bitcoind.GenerateBlocks (BlockHeightOffset32 minimumDepth) walletInstance.Address

            do! walletInstance.WaitForFundingConfirmed channelId

            let! lockFundingRes = Lightning.Network.LockChannelFunding walletInstance.Node channelId
            UnwrapResult lockFundingRes "LockChannelFunding failed"

            let channelInfo = walletInstance.ChannelStore.ChannelInfo channelId
            match channelInfo.Status with
            | ChannelStatus.Active -> channelId |> ignore
            | status -> failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

            return channelId
        }

    let AcceptChannel(walletInstance: WalletInstance) (bitcoind: Bitcoind) (lnd : Lnd): Async<ChannelIdentifier * OutPoint> =
        async {
            do! walletInstance.FundByMining bitcoind lnd

            let acceptChannelTask = Lightning.Network.AcceptChannel walletInstance.Node
            let openChannelTask = async {
                let! connectionResult = lnd.ConnectTo walletInstance.NodeEndPoint
                match connectionResult with
                | ConnectionResult.CouldNotConnect -> failwith "could not connect"
                | _connectionResult -> ()
                return!
                    lnd.OpenChannel
                        walletInstance.NodeEndPoint
                        (Money(0.002m, MoneyUnit.BTC))
                        (FeeRatePerKw 666u)
            }

            let! acceptChannelRes, openChannelRes = AsyncExtensions.MixedParallel2 acceptChannelTask openChannelTask
            let (channelId, _) = UnwrapResult acceptChannelRes "AcceptChannel failed"
            UnwrapResult openChannelRes "lnd.OpenChannel failed"

            // Wait for the funding transaction to appear in mempool
            while bitcoind.GetTxIdsInMempool().Length = 0 do
                do! Async.Sleep 500

            // Mine blocks on top of the funding transaction to make it confirmed.
            bitcoind.GenerateBlocks Config.MinimumDepth walletInstance.Address

            do! walletInstance.WaitForFundingConfirmed channelId

            let! lockFundingRes = Lightning.Network.LockChannelFunding walletInstance.Node channelId
            UnwrapResult lockFundingRes "LockChannelFunding failed"

            let channelInfo = walletInstance.ChannelStore.ChannelInfo channelId
            match channelInfo.Status with
            | ChannelStatus.Active -> ()
            | status -> failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

            // Wait for lnd to realise we're offline
            do! Async.Sleep 1000
            let fundingOutPoint =
                let fundingTxId = uint256(channelInfo.FundingTxId.ToString())
                let fundingOutPointIndex = channelInfo.FundingOutPointIndex
                OutPoint(fundingTxId, fundingOutPointIndex)

            return (channelId , fundingOutPoint)
        }
