namespace GWallet.Backend

open System
open System.Net
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

    // TODO: stop using this method below, in favour of new overloads proposed here: https://github.com/Nethereum/Nethereum/pull/124
    let ToHexString(byteArray: byte array) =
        BitConverter.ToString(byteArray).Replace("-", String.Empty)

    let rec private IsOfTypeOrItsInner<'T>(ex: Exception) =
        if (ex = null) then
            false
        else if (ex.GetType() = typeof<'T>) then
            true
        else
            IsOfTypeOrItsInner<'T>(ex.InnerException)

    let GetBalance(account: IAccount): Option<decimal> =
        let web3 =
            match account.Currency with
            | Currency.ETH -> ethWeb3
            | Currency.ETC -> etcWeb3
            | _ -> failwith("currency unknown")

        let maybeBalance =
            try
                let balanceTask = web3.Eth.GetBalance.SendRequestAsync(account.PublicAddress)
                balanceTask.Wait()
                Some(balanceTask.Result.Value)
            with
            | ex when IsOfTypeOrItsInner<WebException>(ex) -> None

        match maybeBalance with
        | None -> None
        | Some(balance) -> Some(UnitConversion.Convert.FromWei(balance, UnitConversion.EthUnit.Ether))

    let GetAllAccounts(): seq<IAccount> =
        seq {
            let allCurrencies = Enum.GetValues(typeof<Currency>).Cast<Currency>() |> List.ofSeq

            for currency in allCurrencies do
                for account in Config.GetAllAccounts(currency) do
                    yield account
        }

    let EstimateFee (currency: Currency): EtherMinerFee =
        let web3 =
            match currency with
            | Currency.ETH -> ethWeb3
            | Currency.ETC -> etcWeb3
            | _ -> failwith("currency unknown")
        let gasPriceTask = web3.Eth.GasPrice.SendRequestAsync()
        gasPriceTask.Wait()
        let gasPrice = gasPriceTask.Result
        { GasPriceInWei = gasPrice.Value; EstimationTime = DateTime.Now; Currency = currency }

    let SendPayment (account: NormalAccount) (destination: string) (amount: decimal) (password: string) (minerFee: EtherMinerFee) =
        let currency = (account :> IAccount).Currency
        if (minerFee.Currency <> currency) then
            invalidArg "account" "currency of account param must be equal to currency of minerFee param"

        let web3 =
            match currency with
            | Currency.ETH -> ethWeb3
            | Currency.ETC -> etcWeb3
            | _ -> failwith("currency unknown")

        let transCountTask = web3.Eth.Transactions.GetTransactionCount.SendRequestAsync((account :> IAccount).PublicAddress)

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

        let trans = web3.OfflineTransactionSigning.SignTransaction(
                        privKeyInHexString,
                        destination,
                        amountInWei,
                        transCount.Value,

                        // we use the SignTransaction() overload that has these 2 arguments because if we don't, we depend on
                        // how well the defaults are of Geth node we're connected to, e.g. with the myEtherWallet server I
                        // was trying to spend 0.002ETH from an account that had 0.01ETH and it was always failing with the
                        // "Insufficient Funds" error saying it needed 212,000,000,000,000,000 wei (0.212 ETH)...
                        minerFee.GasPriceInWei,
                        minerFee.GAS_COST_FOR_A_NORMAL_ETHER_TRANSACTION)

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

    let AddPublicWatcher currency (publicAddress: string) =
        let readOnlyAccount = ReadOnlyAccount(currency, publicAddress)
        Config.AddReadonly readOnlyAccount
        readOnlyAccount

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
        let account = NormalAccount(currency, accountSerializedJson)
        Config.Add account
        account

