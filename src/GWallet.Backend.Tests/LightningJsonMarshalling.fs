namespace GWallet.Backend.Tests

open System.Net

open NUnit.Framework
open Newtonsoft.Json

open GWallet.Backend.UtxoCoin.Lightning

[<TestFixture>]
type LightningJsonMarshalling() =
    let referenceIPEndPoint = IPEndPoint (IPAddress.Parse "127.0.0.1", 8888)

    [<Test>]
    member __.``can deserialize IPEndPoint``() =
        let json = "[\"127.0.0.1\", 8888]"
        let ipEndPoint = JsonConvert.DeserializeObject<IPEndPoint> (json, SerializedChannel.LightningSerializerSettings)
        Assert.That(ipEndPoint, Is.EqualTo referenceIPEndPoint)

    [<Test>]
    member __.``can deserialize IPEndPoint with newlines``() =
        let json = "[\"127.0.0.1\",\n8888\n]"
        let ipEndPoint = JsonConvert.DeserializeObject<IPEndPoint> (json, SerializedChannel.LightningSerializerSettings)
        Assert.That(ipEndPoint, Is.EqualTo referenceIPEndPoint)
    