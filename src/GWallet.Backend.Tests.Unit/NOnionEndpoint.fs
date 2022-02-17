namespace GWallet.Backend.Tests.Unit

open System

open Newtonsoft.Json
open NUnit.Framework

open GWallet.Backend

[<TestFixture>]
type NOnionEndpoint() =

    [<Test>]
    member __.``parse NOnionIntroductionPoint``() =
        let pubkey = "02e00fa312ec5b4839016ec8ff8e2dcaa39a568c5e9ca0576f8c41518b405300b9"
        let inputAddress = "geewallet+nonion://02e00fa312ec5b4839016ec8ff8e2dcaa39a568c5e9ca0576f8c41518b405300b9@207.180.213.181:443?EncryptionKey=o2MQlGVTMfWVncB6jExucyyJMA4CjvfavgAqKqdz9iY%3d&AuthKey=62x%2bxZF78%2fwyuQ%2fBjlE8KrI5UK7lrzZHw61oPzx36UE%3d&OnionKey=UMUOgpZnQNDddBM6BSh4%2b7qLhi7aLPIX7l6%2bUVi4C2o%3d&Fingerprint=Ysh7mkPIcSHfR%2ft%2b7mypcquSQzI%3d&MasterPublicKey=%2bp%2bvMILlHnBmKBW%2bNZ2RZMym3qXfWvngfG4gil0UyvA%3d"
        let nonionEndPoint = UtxoCoin.Lightning.NOnionEndPoint.Parse Currency.LTC inputAddress

        Assert.That(nonionEndPoint.NodeId.ToString(), Is.EqualTo pubkey)
        Assert.That(nonionEndPoint.IntroductionPoint.Address, Is.EqualTo "207.180.213.181")
        Assert.That(nonionEndPoint.IntroductionPoint.Port, Is.EqualTo 443)
        Assert.That(nonionEndPoint.IntroductionPoint.EncryptionKey, Is.EqualTo "o2MQlGVTMfWVncB6jExucyyJMA4CjvfavgAqKqdz9iY=")
        Assert.That(nonionEndPoint.IntroductionPoint.AuthKey, Is.EqualTo "62x+xZF78/wyuQ/BjlE8KrI5UK7lrzZHw61oPzx36UE=")
        Assert.That(nonionEndPoint.IntroductionPoint.OnionKey, Is.EqualTo "UMUOgpZnQNDddBM6BSh4+7qLhi7aLPIX7l6+UVi4C2o=")
        Assert.That(nonionEndPoint.IntroductionPoint.Fingerprint, Is.EqualTo "Ysh7mkPIcSHfR/t+7mypcquSQzI=")
        Assert.That(nonionEndPoint.IntroductionPoint.MasterPublicKey, Is.EqualTo "+p+vMILlHnBmKBW+NZ2RZMym3qXfWvngfG4gil0UyvA=")
