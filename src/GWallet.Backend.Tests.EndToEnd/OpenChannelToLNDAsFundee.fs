namespace GWallet.Backend.Tests.EndToEnd

open NUnit.Framework
open DotNetLightning.Utils

open GWallet.Backend
open GWallet.Regtest

[<TestFixture>]
type OpenChannelAsFundee() =

    [<SetUp>]
    member __.SetUp () =
        do Config.SetRunModeTesting()

    [<Test>]
    member __.``can accept channel from LND``() =
        async {
            use! walletInstance = WalletInstance.New None None
            use bitcoind = Bitcoind.Start()
            use _electrumServer = ElectrumServer.Start bitcoind
            use! lnd = Lnd.Start bitcoind
           
            let! _channelIdAndFundingOutPoint = GwalletToLndChannelManagement.AcceptChannel walletInstance bitcoind lnd

            return ()
        } |> Async.RunSynchronously
