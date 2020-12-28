namespace GWallet.Backend.Tests.EndToEnd

open NUnit.Framework
open DotNetLightning.Utils

open GWallet.Backend
open GWallet.Regtest


[<TestFixture>]
type OpenChannelAsFunder() =

    [<SetUp>]
    member __.SetUp () =
        do Config.SetRunModeRegTest()

    [<Test>]
    member __.``can open channel to LND``() =
        async {
            use! walletInstance = WalletInstance.New None None
            use bitcoind = Bitcoind.Start()
            use _electrumServer = ElectrumServer.Start bitcoind
            use! lnd = Lnd.Start bitcoind
            
            let! _channelId = GwalletToLndChannelManagement.OpenChannel walletInstance bitcoind lnd

            return ()
        } |> Async.RunSynchronously
