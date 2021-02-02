namespace GWallet.Backend.Tests.EndToEnd

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
type GeewalletToGeewalletFundee() =
    
    [<SetUp>]
    member __.SetUp () =
        do Config.SetRunModeRegTest()
    
    [<Category("GeewalletToGeewalletFundee")>]
    [<Test>]
    member __.``can send/receive monohop payments and close channel (fundee)``() = Async.RunSynchronously <| async {
        use! walletInstance = WalletInstance.New (Some Config.FundeeLightningIPEndpoint) (Some Config.FundeeAccountsPrivateKey)
        let! channelId = walletInstance.AcceptChannelFromFunder()

        let! closeChannelRes = Lightning.Network.AcceptCloseChannel walletInstance.Node channelId
        match closeChannelRes with
        | Ok () -> ()
        | Error err -> failwith (SPrintF1 "failed to accept close channel: %A" err)

        return ()
    }
