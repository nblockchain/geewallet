namespace GWallet.Backend

open System
open System.Linq

open Nethereum.Web3
open Nethereum.Core.Signing.Crypto
open Nethereum.KeyStore

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

    let Create currency password =
        let privateKey = EthECKey.GenerateKey()
        let privateKeyAsBytes = EthECKey.GetPrivateKeyAsBytes(privateKey)

        // FIXME: don't ask me why sometimes this version of NEthereum generates 33 bytes instead of the required 32...
        let privateKeyTrimmed =
            if privateKeyAsBytes.Length = 33 then
                privateKeyAsBytes |> Array.skip 1
            else
                privateKeyAsBytes

        let keyStoreService = KeyStoreService()
        let publicAddress = EthECKey.GetPublicAddress(privateKey)

        let accountSerializedJson = keyStoreService.EncryptAndGenerateDefaultKeyStoreAsJson(password, privateKeyTrimmed, publicAddress)
        let account = { Currency = currency; Json = accountSerializedJson }
        Config.Add account
        account

