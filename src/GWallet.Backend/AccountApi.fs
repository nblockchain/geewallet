namespace GWallet.Backend

open System
open System.Net
open System.Linq
open System.Numerics
open System.IO

open Nethereum.Web3
open Nethereum.Core.Signing.Crypto
open Nethereum.KeyStore
open NBitcoin.Crypto
open Newtonsoft.Json

exception InsufficientFunds
exception InvalidPassword

module AccountApi =

    // TODO: to prevent having MyEtherApi as a SPOF, use more services, like https://infura.io/
    let private PUBLIC_WEB3_API_ETH = "https://api.myetherapi.com/eth" // docs: https://www.myetherapi.com/

    // this below is https://classicetherwallet.com/'s public endpoint (TODO: to prevent having a SPOF, use https://etcchain.com/api/ too)
    let private PUBLIC_WEB3_API_ETC = "https://mewapi.epool.io"

    let private ethWeb3 = Web3(PUBLIC_WEB3_API_ETH)
    let private etcWeb3 = Web3(PUBLIC_WEB3_API_ETC)

    let private Web3(currency: Currency) =
        match currency with
        | Currency.ETH -> ethWeb3
        | Currency.ETC -> etcWeb3
        | _ -> failwith("currency unknown")

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

    let GetBalance(account: IAccount): MaybeCached<decimal> =
        let web3 = Web3(account.Currency)

        let maybeBalance =
            try
                let balanceTask = web3.Eth.GetBalance.SendRequestAsync(account.PublicAddress)
                balanceTask.Wait()
                Some(balanceTask.Result.Value)
            with
            | ex when IsOfTypeOrItsInner<WebException>(ex) -> None

        match maybeBalance with
        | None -> NotFresh(Caching.RetreiveLastBalance(account.PublicAddress))
        | Some(balanceInWei) ->
            let balanceInEth = UnitConversion.Convert.FromWei(balanceInWei, UnitConversion.EthUnit.Ether)
            Caching.StoreLastBalance(account.PublicAddress, balanceInEth)
            Fresh(balanceInEth)

    let GetAllAccounts(): seq<IAccount> =
        seq {
            let allCurrencies = Enum.GetValues(typeof<Currency>).Cast<Currency>() |> List.ofSeq

            for currency in allCurrencies do
                for account in Config.GetAllAccounts(currency) do
                    yield account
        }

    let EstimateFee (currency: Currency): EtherMinerFee =
        let web3 = Web3(currency)
        let gasPriceTask = web3.Eth.GasPrice.SendRequestAsync()
        gasPriceTask.Wait()
        let gasPrice = gasPriceTask.Result
        if (gasPrice.Value > BigInteger(Int64.MaxValue)) then
            failwith (sprintf "GWallet serialization doesn't support such a big integer (%s) for the gas, please report this issue."
                          (gasPrice.Value.ToString()))
        let gasPrice64: Int64 = BigInteger.op_Explicit gasPrice.Value
        { GasPriceInWei = gasPrice64; EstimationTime = DateTime.Now; Currency = currency }

    let private GetTransactionCount (currency: Currency, publicAddress: string) =
        let web3 = Web3(currency)
        let transCountTask = web3.Eth.Transactions.GetTransactionCount.SendRequestAsync(publicAddress)
        transCountTask.Wait()
        transCountTask.Result

    let private BroadcastRawTransaction (web3: Web3) trans =
        let insufficientFundsMsg = "Insufficient funds"
        try
            let sendRawTransTask = web3.Eth.Transactions.SendRawTransaction.SendRequestAsync("0x" + trans)
            sendRawTransTask.Wait()
            let txId = sendRawTransTask.Result
            txId
        with
        | ex when ex.Message.StartsWith(insufficientFundsMsg) || ex.InnerException.Message.StartsWith(insufficientFundsMsg) ->
            raise (InsufficientFunds)

    let BroadcastTransaction (trans: SignedTransaction) =
        let web3 = Web3(trans.TransactionInfo.Proposal.Currency)
        BroadcastRawTransaction web3 trans.RawTransaction

    let SignTransaction (account: NormalAccount)
                        (transCount: BigInteger)
                        (destination: string)
                        (amount: decimal)
                        (minerFee: EtherMinerFee)
                        (password: string) =

        let currency = (account :> IAccount).Currency
        if (minerFee.Currency <> currency) then
            invalidArg "account" "currency of account param must be equal to currency of minerFee param"

        let privKeyInBytes =
            try
                NormalAccount.KeyStoreService.DecryptKeyStoreFromJson(password, account.Json)
            with
            // FIXME: I don't like to parse exception messages... https://github.com/Nethereum/Nethereum/pull/122
            | ex when ex.Message.StartsWith("Cannot derive") ->
                raise (InvalidPassword)

        let privKeyInHexString = ToHexString(privKeyInBytes)
        let amountInWei = UnitConversion.Convert.ToWei(amount, UnitConversion.EthUnit.Ether)

        let web3 = Web3(currency)
        let trans = web3.OfflineTransactionSigning.SignTransaction(
                        privKeyInHexString,
                        destination,
                        amountInWei,
                        transCount,

                        // we use the SignTransaction() overload that has these 2 arguments because if we don't, we depend on
                        // how well the defaults are of Geth node we're connected to, e.g. with the myEtherWallet server I
                        // was trying to spend 0.002ETH from an account that had 0.01ETH and it was always failing with the
                        // "Insufficient Funds" error saying it needed 212,000,000,000,000,000 wei (0.212 ETH)...
                        BigInteger(minerFee.GasPriceInWei),
                        minerFee.GAS_COST_FOR_A_NORMAL_ETHER_TRANSACTION)

        if not (web3.OfflineTransactionSigning.VerifyTransaction(trans)) then
            failwith "Transaction could not be verified?"
        trans

    let SendPayment (account: NormalAccount) (destination: string) (amount: decimal)
                    (password: string) (minerFee: EtherMinerFee) =
        let currency = (account :> IAccount).Currency

        let transCount = GetTransactionCount(currency, (account:>IAccount).PublicAddress)
        let trans = SignTransaction account transCount.Value destination amount minerFee password

        let web3 = Web3(currency)
        BroadcastRawTransaction web3 trans

    let SignUnsignedTransaction account (unsignedTrans: UnsignedTransaction) password =
        let rawTransaction = SignTransaction account
                                 (BigInteger(unsignedTrans.TransactionCount))
                                 unsignedTrans.Proposal.DestinationAddress
                                 unsignedTrans.Proposal.Amount
                                 unsignedTrans.Fee
                                 password
        { TransactionInfo = unsignedTrans; RawTransaction = rawTransaction }

    let SaveSignedTransaction (trans: SignedTransaction) (filePath: string) =
        let json =
            JsonConvert.SerializeObject(trans)
        File.WriteAllText(filePath, json)

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

        let accountSerializedJson =
            NormalAccount.KeyStoreService.EncryptAndGenerateDefaultKeyStoreAsJson(password,
                                                                                  privateKeyTrimmed,
                                                                                  publicAddress)
        let account = NormalAccount(currency, accountSerializedJson)
        Config.Add account
        account

    let SaveUnsignedTransaction (transProposal: UnsignedTransactionProposal) (fee: EtherMinerFee) (filePath: string) =
        let transCount = GetTransactionCount(transProposal.Currency, transProposal.OriginAddress)
        if (transCount.Value > BigInteger(Int64.MaxValue)) then
            failwith (sprintf "GWallet serialization doesn't support such a big integer (%s) for the nonce, please report this issue."
                          (transCount.Value.ToString()))

        let unsignedTransaction =
            {
                Proposal = transProposal;
                TransactionCount = BigInteger.op_Explicit transCount.Value;
                Cache = Caching.GetLastCachedData();
                Fee = fee;
            }
        let json =
            JsonConvert.SerializeObject(unsignedTransaction)
        File.WriteAllText(filePath, json)

    let LoadSignedTransactionFromFile (filePath: string) =
        let signedTransInJson = File.ReadAllText(filePath)

        // TODO: this line below works without the UnionConverter() or any other, should we get rid of it from FSharpUtils then?
        JsonConvert.DeserializeObject<SignedTransaction>(signedTransInJson)

    let LoadUnsignedTransactionFromFile (filePath: string) =
        let unsignedTransInJson = File.ReadAllText(filePath)

        // TODO: this line below works without the UnionConverter() or any other, should we get rid of it from FSharpUtils then?
        JsonConvert.DeserializeObject<UnsignedTransaction>(unsignedTransInJson)

