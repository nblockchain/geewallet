namespace GWallet.Backend

open System

open Nethereum.Web3
open Nethereum.Core.Signing.Crypto

module AccountApi =

    // TODO: propose this func to NEthereum's EthECKey class as method name GetPrivateKeyAsHexString()
    let ToHexString(byteArray: byte array) =
        BitConverter.ToString(byteArray).Replace("-", String.Empty)

    let GetBalance(account: Account) =
        // https://www.myetherapi.com/
        let web3 = Web3("https://api.myetherapi.com/eth")
        let balanceTask = web3.Eth.GetBalance.SendRequestAsync(account.PublicAddress)
        balanceTask.Wait()
        UnitConversion.Convert.FromWei(balanceTask.Result.Value, UnitConversion.EthUnit.Ether)

    let CreateOrGetMainAccount(currency): Account =
        let maybeAccount = Config.GetMainAccount(currency)
        match maybeAccount with
        | Some(account) -> account
        | None ->
            let key = EthECKey.GenerateKey()
            let privKeyBytes = key.GetPrivateKeyAsBytes()
            let privKeyInHex = privKeyBytes |> ToHexString
            let account = { Currency = Currency.ETH; HexPrivateKey = privKeyInHex }
            Config.Add account
            account
