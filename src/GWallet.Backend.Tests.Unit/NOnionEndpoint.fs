namespace GWallet.Backend.Tests.Unit

open System

open Newtonsoft.Json
open NUnit.Framework

open GWallet.Backend

[<TestFixture>]
type NOnionEndpoint() =

    [<Test>]
    member __.``parse NOnionIntroductionPoint``() =
        let pubkey = "03d06758583bb5154774a6eb221b1276c9e82d65bbaceca806d90e20c108f4b1c7"
        let inputAddress = "03d06758583bb5154774a6eb221b1276c9e82d65bbaceca806d90e20c108f4b1c7@gwdllz5g7vky2q4gr45zguvoajzf33czreca3a3exosftx72ekppkuqd.onion:9735"
        let nonionEndPoint = UtxoCoin.Lightning.NOnionEndPoint.Parse Currency.BTC inputAddress

        Assert.That(nonionEndPoint.NodeId.ToString(), Is.EqualTo pubkey)
        Assert.That(nonionEndPoint.Url, Is.EqualTo "gwdllz5g7vky2q4gr45zguvoajzf33czreca3a3exosftx72ekppkuqd.onion:9735")
