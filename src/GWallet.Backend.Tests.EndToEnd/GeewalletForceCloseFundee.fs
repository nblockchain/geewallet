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

[<TestFixture>]
type GeewalletForceCloseFundee() =
    
    [<SetUp>]
    member __.SetUp () =
        Config.SetRunModeRegTest()
    
    [<Category("GeewalletForceCloseFundee")>]
    [<Test>]
    member __.``can send/receive monohop payments and force-close channel (fundee)``() = Async.RunSynchronously <| async {
        use! walletInstance = WalletInstance.New (Some Config.FundeeLightningIPEndpoint) (Some Config.FundeeAccountsPrivateKey)
        let! channelId = walletInstance.AcceptChannelFromFunder()

        let! balanceBeforeFundsReclaimed = walletInstance.GetBalance()

        let rec waitForRemoteForceClose() = async {
            let! closingInfoOpt = walletInstance.ChannelStore.CheckForClosingTx channelId
            match closingInfoOpt with
            | Some (closingTxIdString, Some _closingTxHeight) ->
                return!
                    Lightning.Network.CreateRecoveryTxForRemoteForceClose
                        walletInstance.Node
                        channelId
                        closingTxIdString
                        false
            | _ ->
                do! Async.Sleep 2000
                return! waitForRemoteForceClose()
        }
        let! recoveryTxStringOpt = waitForRemoteForceClose()
        let recoveryTxString = UnwrapOption recoveryTxStringOpt "no funds could be recovered"
        let! _recoveryTxId =
            UtxoCoin.Account.BroadcastRawTransaction
                Currency.BTC
                recoveryTxString

        Infrastructure.LogDebug ("waiting for our wallet balance to increase")
        let! _balanceAfterFundsReclaimed =
            let amount = balanceBeforeFundsReclaimed+ Money(1.0m, MoneyUnit.Satoshi)
            walletInstance.WaitForBalance amount

        return ()
    }
