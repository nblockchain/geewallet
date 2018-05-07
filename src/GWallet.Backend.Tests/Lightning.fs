namespace GWallet.Backend.Tests

open NUnit.Framework

open NBitcoin

open GWallet.Backend.UtxoCoin.Lightning

module Lightning =

    [<Test>]
    let ``can create bitcoin script``() =
        let s = Script("OP_DUP OP_HASH160 e0e410b8ce34f7073aae017cf906f03de2b08aed OP_EQUALVERIFY OP_CHECKSIG")
        ()

    [<Test>]
    let ``can convert from Ivy-template script to bitcoin script``() =
        (* ivy source code: ( https://docs.ivy-lang.org/bitcoin/language/ExampleContracts.html#revealpreimage )
             ```
                contract RevealPreimage(hash: Sha256(Bytes), val: Value) {
                  clause reveal(string: Bytes) {
                    verify sha256(string) == hash
                    unlock val
                  }
                }
             ```
         *)
        // result of ivy: SHA256 PUSH(hash) EQUAL
        let ivyScript = IvyTemplateScript("SHA256 PUSH(hash) EQUAL")
        let values = [("hash", "e0e410b8ce34f7073aae017cf906f03de2b08aed")] |> Map.ofSeq
        let bitcoinScript = ivyScript.Instantiate(values)
        Assert.That(bitcoinScript, Is.EqualTo("OP_SHA256 e0e410b8ce34f7073aae017cf906f03de2b08aed OP_EQUAL"))
        let parsedScript = Script(bitcoinScript)
        ()


    [<Test>]
    let ``throws error when value not found in template``() =
        let ivyScript = IvyTemplateScript("SHA256 PUSH(foo) EQUAL")
        let values = [("bar", "e0e410b8ce34f7073aae017cf906f03de2b08aed")] |> Map.ofSeq
        Assert.Throws<TemplateInstantiationException> (fun _ ->
            let bitcoinScript = ivyScript.Instantiate(values)
            Assert.That(bitcoinScript, Is.EqualTo("OP_SHA256 e0e410b8ce34f7073aae017cf906f03de2b08aed OP_EQUAL"))
            let parsedScript = Script(bitcoinScript)
            ()
        ) |> ignore

        ()

    [<Test>]
    let ``throws error when not enough values provided for template``() =
        let ivyScript = IvyTemplateScript("SHA256 PUSH(foo) PUSH(bar) EQUAL")
        let values = [("foo", "e0e410b8ce34f7073aae017cf906f03de2b08aed")] |> Map.ofSeq
        Assert.Throws<TemplateInstantiationException> (fun _ ->
            let bitcoinScript = ivyScript.Instantiate(values)
            Assert.That(bitcoinScript, Is.EqualTo("OP_SHA256 e0e410b8ce34f7073aae017cf906f03de2b08aed OP_EQUAL"))
            let parsedScript = Script(bitcoinScript)
            ()
        ) |> ignore

        ()
