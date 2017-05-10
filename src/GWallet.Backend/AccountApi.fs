namespace GWallet.Backend

open System
open Nethereum.Core.Signing.Crypto

module AccountApi =

    // TODO: propose this func to NEthereum's EthECKey class as method name GetPrivateKeyAsHexString()
    let ToHexString(byteArray: byte array) =
        BitConverter.ToString(byteArray).Replace("-", String.Empty)

    let CreateAccount(currency): Account =
        let key = EthECKey.GenerateKey()
        let privKeyBytes = key.GetPrivateKeyAsBytes()
        let privKeyInHex = privKeyBytes |> ToHexString
        let account = { Currency = "ETH"; HexPrivateKey = privKeyInHex }
        Config.Add account
        account
