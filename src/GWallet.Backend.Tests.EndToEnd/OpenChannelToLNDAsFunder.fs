namespace GWallet.Backend.Tests.EndToEnd

open NUnit.Framework
open DotNetLightning.Utils

open GWallet.Backend


[<TestFixture>]
type OpenChannelAsFunder() =

    [<SetUp>]
    member __.SetUp () =
        do Config.SetRunModeTesting()

    [<Test>]
    member __.``can open channel from LND``() =
        async {
            use! walletInstance = WalletInstance.New None None
            use bitcoind = Bitcoind.Start()
            use _electrumServer = ElectrumServer.Start bitcoind
            use! lnd = Lnd.Start bitcoind
            
            let! _channelId = ChannelManagement.OpenChannel walletInstance bitcoind lnd

            return ()
        } |> Async.RunSynchronously