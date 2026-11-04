namespace GWallet.Backend.Tests

open System
open System.Linq

open NUnit.Framework

open GWallet.Backend
open GWallet.Backend.UtxoCoin

[<TestFixture>]
type ElectrumServer() =

    [<Test>]
    member __.``filters electrum BTC servers``() =
        for electrumServer in ElectrumServerSeedList.DefaultBtcList do
            Assert.That (electrumServer.ServerInfo.ConnectionType.Encrypted, Is.EqualTo false,
                sprintf "BTC servers list should be filtered against only-TLS compatible servers, but %s was found"
                        electrumServer.ServerInfo.NetworkPath)

            Assert.That (electrumServer.ServerInfo.NetworkPath, IsString.WhichDoesNotEndWith ".onion",
                sprintf "BTC servers list should be filtered against onion servers, but %s was found"
                        electrumServer.ServerInfo.NetworkPath)

    [<Test>]
    member __.``filters electrum LTC servers``() =
        for electrumServer in ElectrumServerSeedList.DefaultLtcList do
            Assert.That (electrumServer.ServerInfo.ConnectionType.Encrypted, Is.EqualTo false,
                sprintf "BTC servers list should be filtered against only-TLS compatible servers, but %s was found"
                        electrumServer.ServerInfo.NetworkPath)

            Assert.That (electrumServer.ServerInfo.NetworkPath, IsString.WhichDoesNotEndWith ".onion",
                sprintf "BTC servers list should be filtered against onion servers, but %s was found"
                        electrumServer.ServerInfo.NetworkPath)
