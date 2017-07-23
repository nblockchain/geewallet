namespace GWallet.Backend

open System
open System.Net
open System.Linq
open System.Numerics
open System.IO

open Nethereum.Web3
open Nethereum.Signer
open Nethereum.KeyStore
open Nethereum.Util
open Nethereum.KeyStore.Crypto
open Newtonsoft.Json

exception InsufficientFunds
exception InvalidPassword
exception DestinationEqualToOrigin

module Account =

    let private currencyAgnosticWeb3 = Web3()

    let rec private IsOfTypeOrItsInner<'T>(ex: Exception) =
        if (ex = null) then
            false
        else if (ex.GetType() = typeof<'T>) then
            true
        else
            IsOfTypeOrItsInner<'T>(ex.InnerException)

    let GetBalance(account: IAccount): MaybeCached<decimal> =
        let maybeBalance =
            try
                let balance =
                    EtherServer.GetBalance account.Currency account.PublicAddress
                Some(balance.Value)
            with
            | ex when IsOfTypeOrItsInner<WebException>(ex) -> None

        match maybeBalance with
        | None -> NotFresh(Caching.RetreiveLastBalance(account.PublicAddress))
        | Some(balanceInWei) ->
            let balanceInEth = UnitConversion.Convert.FromWei(balanceInWei, UnitConversion.EthUnit.Ether)
            Caching.StoreLastBalance(account.PublicAddress, balanceInEth)
            Fresh(balanceInEth)

    let GetAllActiveAccounts(): seq<IAccount> =
        seq {
            let allCurrencies = Currency.GetAll()

            for currency in allCurrencies do
                for account in Config.GetAllActiveAccounts(currency) do
                    yield account
        }

    let GetArchivedAccountsWithPositiveBalance(): seq<ArchivedAccount*decimal> =
        seq {
            let allCurrencies = Currency.GetAll()

            for currency in allCurrencies do
                for account in Config.GetAllArchivedAccounts(currency) do
                    match GetBalance(account) with
                    | NotFresh(NotAvailable) -> ()
                    | Fresh(balance) ->
                        if (balance > 0m) then
                            yield account,balance
                    | NotFresh(Cached(balance,time)) ->
                        () // TODO: do something in this case??
        }

    let EstimateFee (currency: Currency): EtherMinerFee =
        let gasPrice = EtherServer.GetGasPrice currency
        if (gasPrice.Value > BigInteger(Int64.MaxValue)) then
            failwith (sprintf "GWallet serialization doesn't support such a big integer (%s) for the gas, please report this issue."
                          (gasPrice.Value.ToString()))
        let gasPrice64: Int64 = BigInteger.op_Explicit gasPrice.Value
        { GasPriceInWei = gasPrice64; EstimationTime = DateTime.Now; Currency = currency }

    let private GetTransactionCount (currency: Currency, publicAddress: string) =
        EtherServer.GetTransactionCount currency publicAddress

    let private BroadcastRawTransaction (currency: Currency) trans =
        let insufficientFundsMsg = "Insufficient funds"
        try
            let txId = EtherServer.BroadcastTransaction currency ("0x" + trans)
            txId
        with
        | ex when ex.Message.StartsWith(insufficientFundsMsg) || ex.InnerException.Message.StartsWith(insufficientFundsMsg) ->
            raise (InsufficientFunds)

    let BroadcastTransaction (trans: SignedTransaction) =
        BroadcastRawTransaction
            trans.TransactionInfo.Proposal.Currency
            trans.RawTransaction

    let internal GetPrivateKey (account: NormalAccount) password =
        let privKeyInBytes =
            try
                NormalAccount.KeyStoreService.DecryptKeyStoreFromJson(password, account.Json)
            with
            | :? DecryptionException ->
                raise (InvalidPassword)

        EthECKey(privKeyInBytes, true)

    let private SignTransactionWithPrivateKey (account: IAccount)
                                              (transCount: BigInteger)
                                              (destination: string)
                                              (amount: decimal)
                                              (minerFee: EtherMinerFee)
                                              (privateKey: EthECKey) =

        let currency = account.Currency
        if (minerFee.Currency <> currency) then
            invalidArg "account" "currency of account param must be equal to currency of minerFee param"

        let amountInWei = UnitConversion.Convert.ToWei(amount, UnitConversion.EthUnit.Ether)

        let privKeyInBytes = privateKey.GetPrivateKeyAsBytes()
        let trans = currencyAgnosticWeb3.OfflineTransactionSigner.SignTransaction(
                        privKeyInBytes,
                        destination,
                        amountInWei,
                        transCount,

                        // we use the SignTransaction() overload that has these 2 arguments because if we don't, we depend on
                        // how well the defaults are of Geth node we're connected to, e.g. with the myEtherWallet server I
                        // was trying to spend 0.002ETH from an account that had 0.01ETH and it was always failing with the
                        // "Insufficient Funds" error saying it needed 212,000,000,000,000,000 wei (0.212 ETH)...
                        BigInteger(minerFee.GasPriceInWei),
                        minerFee.GAS_COST_FOR_A_NORMAL_ETHER_TRANSACTION)

        if not (currencyAgnosticWeb3.OfflineTransactionSigner.VerifyTransaction(trans)) then
            failwith "Transaction could not be verified?"
        trans

    let SignTransaction (account: NormalAccount)
                        (transCount: BigInteger)
                        (destination: string)
                        (amount: decimal)
                        (minerFee: EtherMinerFee)
                        (password: string) =

        let privateKey = GetPrivateKey account password
        SignTransactionWithPrivateKey account transCount destination amount minerFee privateKey

    let Archive (account: NormalAccount)
                (password: string) =
        let privateKey = GetPrivateKey account password
        let newArchivedAccount = ArchivedAccount((account:>IAccount).Currency, privateKey)
        Config.AddArchived newArchivedAccount
        Config.RemoveNormal account

    let SweepArchivedFunds (account: ArchivedAccount)
                           (balance: decimal)
                           (destination: IAccount)
                           (minerFee: EtherMinerFee) =
        let accountFrom = (account:>IAccount)
        let transCountHexBigInt = GetTransactionCount (accountFrom.Currency, accountFrom.PublicAddress)
        let transCount = transCountHexBigInt.Value
        let amount = balance - minerFee.EtherPriceForNormalTransaction()
        let signedTrans = SignTransactionWithPrivateKey
                              account transCount destination.PublicAddress amount minerFee account.PrivateKey
        BroadcastRawTransaction accountFrom.Currency signedTrans

    let SendPayment (account: NormalAccount) (destination: string) (amount: decimal)
                    (password: string) (minerFee: EtherMinerFee) =
        let baseAccount = account :> IAccount
        if (baseAccount.PublicAddress.Equals(destination, StringComparison.InvariantCultureIgnoreCase)) then
            raise DestinationEqualToOrigin

        let currency = baseAccount.Currency

        let transCount = GetTransactionCount(currency, (account:>IAccount).PublicAddress)
        let trans = SignTransaction account transCount.Value destination amount minerFee password

        BroadcastRawTransaction currency trans

    let SignUnsignedTransaction account (unsignedTrans: UnsignedTransaction) password =
        let rawTransaction = SignTransaction account
                                 (BigInteger(unsignedTrans.TransactionCount))
                                 unsignedTrans.Proposal.DestinationAddress
                                 unsignedTrans.Proposal.Amount
                                 unsignedTrans.Fee
                                 password
        { TransactionInfo = unsignedTrans; RawTransaction = rawTransaction }

    let public ExportSignedTransaction (trans: SignedTransaction) =
        Marshalling.Serialize trans

    let SaveSignedTransaction (trans: SignedTransaction) (filePath: string) =
        let json = ExportSignedTransaction trans
        File.WriteAllText(filePath, json)

    let AddPublicWatcher currency (publicAddress: string) =
        let readOnlyAccount = ReadOnlyAccount(currency, publicAddress)
        Config.AddReadonly readOnlyAccount
        readOnlyAccount

    let RemovePublicWatcher (account: ReadOnlyAccount) =
        Config.RemoveReadonly account

    let Create currency password =
        let privateKey = EthECKey.GenerateKey()
        let privateKeyAsBytes = privateKey.GetPrivateKeyAsBytes()

        // FIXME: don't ask me why sometimes this version of NEthereum generates 33 bytes instead of the required 32...
        let privateKeyTrimmed =
            if privateKeyAsBytes.Length = 33 then
                privateKeyAsBytes |> Array.skip 1
            else
                privateKeyAsBytes

        let publicAddress = privateKey.GetPublicAddress()

        let accountSerializedJson =
            NormalAccount.KeyStoreService.EncryptAndGenerateDefaultKeyStoreAsJson(password,
                                                                                  privateKeyTrimmed,
                                                                                  publicAddress)
        Config.AddNormalAccount currency accountSerializedJson

    let public ExportUnsignedTransactionToJson trans =
        Marshalling.Serialize trans

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
        let json = ExportUnsignedTransactionToJson unsignedTransaction
        File.WriteAllText(filePath, json)

    let public ImportUnsignedTransactionFromJson (json: string): UnsignedTransaction =
        Marshalling.Deserialize json

    let public ImportSignedTransactionFromJson (json: string): SignedTransaction =
        Marshalling.Deserialize json

    let LoadSignedTransactionFromFile (filePath: string) =
        let signedTransInJson = File.ReadAllText(filePath)

        ImportSignedTransactionFromJson signedTransInJson

    let LoadUnsignedTransactionFromFile (filePath: string) =
        let unsignedTransInJson = File.ReadAllText(filePath)

        ImportUnsignedTransactionFromJson unsignedTransInJson

