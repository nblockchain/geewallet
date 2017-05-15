namespace GWallet.Backend

open System
open System.Linq
open System.Numerics

open Nethereum.Web3
open Nethereum.Core.Signing.Crypto
open Nethereum.KeyStore
open NBitcoin.Crypto


exception InsufficientFunds
exception InvalidPassword

module AccountApi =

    // TODO: to prevent having MyEtherApi as a SPOF, use more services, like https://infura.io/
    let private PUBLIC_WEB3_API_ETH = "https://api.myetherapi.com/eth" // docs: https://www.myetherapi.com/

    let private PUBLIC_WEB3_API_ETC = "https://mewapi.epool.io"

    let private ethWeb3 = Web3(PUBLIC_WEB3_API_ETH)
    let private etcWeb3 = Web3(PUBLIC_WEB3_API_ETC)

    let private keyStoreService = KeyStoreService()

    // TODO: propose this func to NEthereum's EthECKey class as method name GetPrivateKeyAsHexString()
    let ToHexString(byteArray: byte array) =
        BitConverter.ToString(byteArray).Replace("-", String.Empty)

    let GetBalance(account: Account) =
        let web3 =
            match account.Currency with
            | Currency.ETH -> ethWeb3
            | Currency.ETC -> etcWeb3
            | _ -> failwith("currency unknown")

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

    let private GAS_COST_FOR_A_NORMAL_ETHER_TRANSACTION = 21000

    let SendPayment (account: Account) (destination: string) (amount: decimal) (password: string) =
        let web3 =
            match account.Currency with
            | Currency.ETH -> ethWeb3
            | Currency.ETC -> etcWeb3
            | _ -> failwith("currency unknown")
        let gasPriceTask = web3.Eth.GasPrice.SendRequestAsync()

        let transCountTask = web3.Eth.Transactions.GetTransactionCount.SendRequestAsync(account.PublicAddress)

        let privKeyInBytes =
            try
                keyStoreService.DecryptKeyStoreFromJson(password, account.Json)
            with
            // FIXME: I don't like to parse exception messages... https://github.com/Nethereum/Nethereum/pull/122
            | ex when ex.Message.StartsWith("Cannot derive") ->
                raise (InvalidPassword)

        let privKeyInHexString = ToHexString(privKeyInBytes)
        let amountInWei = UnitConversion.Convert.ToWei(amount, UnitConversion.EthUnit.Ether)

        transCountTask.Wait()
        let transCount = transCountTask.Result

        gasPriceTask.Wait()
        let gasPrice = gasPriceTask.Result
        let trans = web3.OfflineTransactionSigning.SignTransaction(privKeyInHexString,
                                                                   destination,
                                                                   amountInWei,
                                                                   transCount.Value,
                                                                   gasPrice.Value,
                                                                   BigInteger(GAS_COST_FOR_A_NORMAL_ETHER_TRANSACTION))
        if not (web3.OfflineTransactionSigning.VerifyTransaction(trans)) then
            failwith "Transaction could not be verified?"

        let insufficientFundsMsg = "Insufficient funds"
        try
            let sendRawTransTask = web3.Eth.Transactions.SendRawTransaction.SendRequestAsync("0x" + trans)
            sendRawTransTask.Wait()
            let txId = sendRawTransTask.Result
            txId
        with
        | ex when ex.Message.StartsWith(insufficientFundsMsg) || ex.InnerException.Message.StartsWith(insufficientFundsMsg) ->
            raise (InsufficientFunds)

    let Create currency password =
        let privateKey = EthECKey.GenerateKey()
        let privateKeyAsBytes = EthECKey.GetPrivateKeyAsBytes(privateKey)

        // FIXME: don't ask me why sometimes this version of NEthereum generates 33 bytes instead of the required 32...
        let privateKeyTrimmed =
            if privateKeyAsBytes.Length = 33 then
                privateKeyAsBytes |> Array.skip 1
            else
                privateKeyAsBytes

        let publicAddress = EthECKey.GetPublicAddress(privateKey)

        let accountSerializedJson = keyStoreService.EncryptAndGenerateDefaultKeyStoreAsJson(password, privateKeyTrimmed, publicAddress)
        let account = { Currency = currency; Json = accountSerializedJson }
        Config.Add account
        account

