namespace GWallet.Backend

open System
open System.Linq

open Nethereum.Web3
open Nethereum.Core.Signing.Crypto

module AccountApi =

    // TODO: to prevent having MyEtherApi as a SPOF, use more services, like https://infura.io/
    let private PUBLIC_WEB3_API_ETH = "https://api.myetherapi.com/eth" // docs: https://www.myetherapi.com/

    let private PUBLIC_WEB3_API_ETC = "https://mewapi.epool.io"

    // TODO: propose this func to NEthereum's EthECKey class as method name GetPrivateKeyAsHexString()
    let ToHexString(byteArray: byte array) =
        BitConverter.ToString(byteArray).Replace("-", String.Empty)

    let GetBalance(account: Account) =
        let web3ApiUrl =
            match account.Currency with
            | Currency.ETH -> PUBLIC_WEB3_API_ETH
            | Currency.ETC -> PUBLIC_WEB3_API_ETC
            | _ -> failwith("currency unknown")

        let web3 = Web3(web3ApiUrl)
        let balanceTask = web3.Eth.GetBalance.SendRequestAsync(account.PublicAddress)
        balanceTask.Wait()
        UnitConversion.Convert.FromWei(balanceTask.Result.Value, UnitConversion.EthUnit.Ether)

    let GetAllAccounts(): seq<Account> =
        seq {
            let allCurrencies = Enum.GetValues(typeof<Currency>).Cast<Currency>() |> List.ofSeq

            for currency in allCurrencies do
                for account in Config.GetAllAccounts(currency) do
                    yield account
        }

    let Create(currency): Account =
        let key = EthECKey.GenerateKey()
        let privKeyBytes = key.GetPrivateKeyAsBytes()
        let privKeyInHex = privKeyBytes |> ToHexString
        let account = { Id = Guid.NewGuid(); Currency = currency; HexPrivateKey = privKeyInHex }
        Config.Add account
        account

