namespace GWallet.Backend

open System
open System.Linq
open System.IO

module Account =

    let private GetBalanceInternal(account: IAccount) (onlyConfirmed: bool) (mode: Mode): Async<decimal> =
        async {
            if account.Currency.IsEtherBased() then
                if (onlyConfirmed) then
                    return! Ether.Account.GetConfirmedBalance account mode
                else
                    return! Ether.Account.GetUnconfirmedPlusConfirmedBalance account mode
            elif (account.Currency.IsUtxo()) then
                if (onlyConfirmed) then
                    return! UtxoCoin.Account.GetConfirmedBalance account mode
                else
                    return! UtxoCoin.Account.GetUnconfirmedPlusConfirmedBalance account mode
            else
                return failwith (sprintf "Unknown currency %A" account.Currency)
        }

    let private GetBalanceFromServer (account: IAccount) (onlyConfirmed: bool) (mode: Mode)
                                         : Async<Option<decimal>> =
        async {
            try
                let! balance = GetBalanceInternal account onlyConfirmed mode
                return Some balance
            with
            | ex ->
                if (FSharpUtil.FindException<ServerUnavailabilityException> ex).IsSome then
                    return None
                else
                    return raise (FSharpUtil.ReRaise ex)
        }

    let GetUnconfirmedPlusConfirmedBalance(account: IAccount) =
        GetBalanceFromServer account false

    let GetConfirmedBalance(account: IAccount) =
        GetBalanceFromServer account true

    let private GetShowableBalanceAndImminentPaymentInternal (account: IAccount) (mode: Mode)
                                                                 : Async<Option<decimal*bool>> = async {

        let! confirmed = GetConfirmedBalance account mode
        if mode = Mode.Fast && Caching.Instance.FirstRun then
            match confirmed with
            | None -> return None
            | Some confirmedAmount -> return Some(confirmedAmount, false)
        else
            let! unconfirmed = GetUnconfirmedPlusConfirmedBalance account mode
            match unconfirmed,confirmed with
            | Some unconfirmedAmount,Some confirmedAmount ->
                if (unconfirmedAmount > confirmedAmount) then
                    return Some (confirmedAmount, true)
                else
                    return Some (unconfirmedAmount, false)
            | _ ->
                match confirmed with
                | None -> return None
                | Some confirmedAmount -> return Some(confirmedAmount, false)
    }

    let GetShowableBalanceAndImminentPayment (account: IAccount) (mode: Mode): Async<MaybeCached<decimal>*bool> =
        async {
            let! maybeBalanceAndImminentPayment = GetShowableBalanceAndImminentPaymentInternal account mode
            match maybeBalanceAndImminentPayment with
            | None ->
                let cachedBalance = Caching.Instance.RetreiveLastCompoundBalance account.PublicAddress account.Currency
                return (NotFresh cachedBalance, false)
            | Some (balance,imminentPayment) ->
                let compoundBalance,_ =
                    Caching.Instance.RetreiveAndUpdateLastCompoundBalance account.PublicAddress
                                                                          account.Currency
                                                                          balance
                return (Fresh compoundBalance, imminentPayment)
        }

    let mutable wiped = false
    let private WipeConfig(allCurrencies: seq<Currency>) =
#if DEBUG
        if not wiped then
            for currency in allCurrencies do
                Config.Wipe currency
            wiped <- true
#else
        ()
#endif

    let GetAllActiveAccounts(): list<IAccount> =
        let allCurrencies = Currency.GetAll()

// uncomment this block below, manually, if when testing you need to go back to test the WelcomePage.xaml
#if FALSE
        WipeConfig allCurrencies
        Caching.Instance.ClearAll()
#endif

        seq {
            for currency in allCurrencies do
                for accountFile in Config.GetAllReadOnlyAccounts(currency) do
                    let fileName = Path.GetFileName(accountFile.FullName)
                    yield ReadOnlyAccount(currency, fileName) :> IAccount

                let fromAccountFileToPublicAddress =
                    if currency.IsUtxo() then
                        UtxoCoin.Account.GetPublicAddressFromAccountFile currency
                    elif currency.IsEtherBased() then
                        Ether.Account.GetPublicAddressFromAccountFile
                    else
                        failwith (sprintf "Unknown currency %A" currency)
                for accountFile in Config.GetAllNormalAccounts(currency) do
                    yield NormalAccount(currency, accountFile, fromAccountFileToPublicAddress) :> IAccount

        } |> List.ofSeq

    let GetArchivedAccountsWithPositiveBalance(): Async<seq<ArchivedAccount*decimal>> =
        let asyncJobs = seq<Async<ArchivedAccount*Option<decimal>>> {
            let allCurrencies = Currency.GetAll()

            for currency in allCurrencies do
                let fromAccountFileToPublicAddress =
                    if currency.IsUtxo() then
                        UtxoCoin.Account.GetPublicAddressFromUnencryptedPrivateKey currency
                    elif currency.IsEtherBased() then
                        Ether.Account.GetPublicAddressFromUnencryptedPrivateKey
                    else
                        failwith (sprintf "Unknown currency %A" currency)

                for accountFile in Config.GetAllArchivedAccounts(currency) do
                    let account = ArchivedAccount(currency, accountFile, fromAccountFileToPublicAddress)
                    let maybeBalance = GetUnconfirmedPlusConfirmedBalance account Mode.Fast
                    yield async {
                        let! unconfirmedBalance = maybeBalance
                        let positiveBalance =
                            match unconfirmedBalance with
                            | Some balance ->
                                if (balance > 0m) then
                                    Some(balance)
                                else
                                    None
                            | _ ->
                                None
                        return account,positiveBalance
                    }
        }
        let executedBalances = Async.Parallel asyncJobs
        async {
            let! accountAndPositiveBalances = executedBalances
            return seq {
                for account,maybePositiveBalance in accountAndPositiveBalances do
                    match maybePositiveBalance with
                    | Some positiveBalance -> yield account,positiveBalance
                    | _ -> ()
            }
        }

    // TODO: add tests for these (just in case address validation breaks after upgrading our dependencies)
    let ValidateAddress (currency: Currency) (address: string) =
        if currency.IsEtherBased() then
            Ether.Account.ValidateAddress currency address
        elif currency.IsUtxo() then
            UtxoCoin.Account.ValidateAddress currency address
        else
            failwith (sprintf "Unknown currency %A" currency)

    let ValidateUnknownCurrencyAddress (address: string): List<Currency> =
        if address.StartsWith "0x" then
            let someEtherCurrency = Currency.ETC
            Ether.Account.ValidateAddress someEtherCurrency address
            let allEtherCurrencies = [ Currency.ETC; Currency.ETH; Currency.DAI ]
            allEtherCurrencies
        elif (address.StartsWith "L" || address.StartsWith "M") then
            let ltc = Currency.LTC
            UtxoCoin.Account.ValidateAddress ltc address
            [ ltc ]
        else
            let btc = Currency.BTC
            UtxoCoin.Account.ValidateAddress btc address
            [ btc ]

    let EstimateFee account (amount: TransferAmount) destination: Async<IBlockchainFeeInfo> =
        let currency = (account:>IAccount).Currency
        async {
            if currency.IsUtxo() then
                let! fee = UtxoCoin.Account.EstimateFee account amount destination
                return fee :> IBlockchainFeeInfo
            elif currency.IsEtherBased() then
                let! fee = Ether.Account.EstimateFee account amount destination
                return fee :> IBlockchainFeeInfo
            else
                return failwith (sprintf "Unknown currency %A" currency)
        }

    // FIXME: broadcasting shouldn't just get N consistent replies from FaultToretantClient,
    // but send it to as many as possible, otherwise it could happen that some server doesn't
    // broadcast it even if you sent it
    let BroadcastTransaction (trans: SignedTransaction<_>): Async<Uri> =
        async {
            let currency = trans.TransactionInfo.Proposal.Amount.Currency

            let! txId =
                if currency.IsEtherBased() then
                    Ether.Account.BroadcastTransaction trans
                elif currency.IsUtxo() then
                    UtxoCoin.Account.BroadcastTransaction currency trans
                else
                    failwith (sprintf "Unknown currency %A" currency)

            let feeCurrency = trans.TransactionInfo.Metadata.Currency
            Caching.Instance.StoreOutgoingTransaction
                trans.TransactionInfo.Proposal.OriginAddress
                currency
                feeCurrency
                txId
                trans.TransactionInfo.Proposal.Amount.ValueToSend
                trans.TransactionInfo.Metadata.FeeValue

            return BlockExplorer.GetTransaction currency txId
        }

    let SignTransaction (account: NormalAccount)
                        (destination: string)
                        (amount: TransferAmount)
                        (transactionMetadata: IBlockchainFeeInfo)
                        (password: string) =

        match transactionMetadata with
        | :? Ether.TransactionMetadata as etherTxMetada ->
            Ether.Account.SignTransaction
                  account
                  etherTxMetada
                  destination
                  amount
                  password
        | :? UtxoCoin.TransactionMetadata as btcTxMetadata ->
            UtxoCoin.Account.SignTransaction
                account
                btcTxMetadata
                destination
                amount
                password
        | _ -> failwith "fee type unknown"

    let private CreateArchivedAccount (currency: Currency) (unencryptedPrivateKey: string): ArchivedAccount =
        let fromUnencryptedPrivateKeyToPublicAddressFunc =
            if currency.IsUtxo() then
                UtxoCoin.Account.GetPublicAddressFromUnencryptedPrivateKey currency
            elif currency.IsEther() then
                Ether.Account.GetPublicAddressFromUnencryptedPrivateKey
            else
                failwith (sprintf "Unknown currency %A" currency)
        let fileName = fromUnencryptedPrivateKeyToPublicAddressFunc unencryptedPrivateKey
        let newAccountFile = Config.AddArchivedAccount currency fileName unencryptedPrivateKey
        ArchivedAccount(currency, newAccountFile, fromUnencryptedPrivateKeyToPublicAddressFunc)

    let Archive (account: NormalAccount)
                (password: string)
                : unit =
        let currency = (account:>IAccount).Currency
        let privateKeyAsString =
            if currency.IsUtxo() then
                let privKey = UtxoCoin.Account.GetPrivateKey account password
                privKey.GetWif(UtxoCoin.Account.GetNetwork currency).ToWif()
            elif currency.IsEther() then
                let privKey = Ether.Account.GetPrivateKey account password
                privKey.GetPrivateKey()
            else
                failwith (sprintf "Unknown currency %A" currency)
        CreateArchivedAccount currency privateKeyAsString |> ignore
        Config.RemoveNormal account

    let SweepArchivedFunds (account: ArchivedAccount)
                           (balance: decimal)
                           (destination: IAccount)
                           (txMetadata: IBlockchainFeeInfo) =
        match txMetadata with
        | :? Ether.TransactionMetadata as etherTxMetadata ->
            Ether.Account.SweepArchivedFunds account balance destination etherTxMetadata
        | :? UtxoCoin.TransactionMetadata as btcTxMetadata ->
            UtxoCoin.Account.SweepArchivedFunds account balance destination btcTxMetadata
        | _ -> failwith "tx metadata type unknown"

    let SendPayment (account: NormalAccount)
                    (txMetadata: IBlockchainFeeInfo)
                    (destination: string)
                    (amount: TransferAmount)
                    (password: string)
                    =
        let baseAccount = account :> IAccount
        if (baseAccount.PublicAddress.Equals(destination, StringComparison.InvariantCultureIgnoreCase)) then
            raise DestinationEqualToOrigin

        let currency = baseAccount.Currency
        ValidateAddress currency destination

        async {
            let! txId =
                if currency.IsUtxo() then
                    match txMetadata with
                    | :? UtxoCoin.TransactionMetadata as btcTxMetadata ->
                        UtxoCoin.Account.SendPayment account btcTxMetadata destination amount password
                    | _ -> failwith "fee for BTC currency should be Bitcoin.MinerFee type"
                elif currency.IsEtherBased() then
                    match txMetadata with
                    | :? Ether.TransactionMetadata as etherTxMetadata ->
                        Ether.Account.SendPayment account etherTxMetadata destination amount password
                    | _ -> failwith "fee for Ether currency should be EtherMinerFee type"
                else
                    failwith (sprintf "Unknown currency %A" currency)

            let feeCurrency = txMetadata.Currency
            Caching.Instance.StoreOutgoingTransaction
                baseAccount.PublicAddress
                currency
                feeCurrency
                txId
                amount.ValueToSend
                txMetadata.FeeValue

            return BlockExplorer.GetTransaction currency txId
        }

    let SignUnsignedTransaction (account)
                                (unsignedTrans: UnsignedTransaction<IBlockchainFeeInfo>)
                                password =
        let rawTransaction = SignTransaction account
                                 unsignedTrans.Proposal.DestinationAddress
                                 unsignedTrans.Proposal.Amount
                                 unsignedTrans.Metadata
                                 password

        { TransactionInfo = unsignedTrans; RawTransaction = rawTransaction }

    let private ExportSignedTransaction (trans: SignedTransaction<_>) =
        Marshalling.Serialize trans

    let private SerializeSignedTransactionPlain (trans: SignedTransaction<_>): string =
        let json =
            match trans.TransactionInfo.Metadata.GetType() with
            | t when t = typeof<Ether.TransactionMetadata> ->
                let unsignedEthTx = {
                    Metadata = box trans.TransactionInfo.Metadata :?> Ether.TransactionMetadata;
                    Proposal = trans.TransactionInfo.Proposal;
                    Cache = trans.TransactionInfo.Cache;
                }
                let signedEthTx = {
                    TransactionInfo = unsignedEthTx;
                    RawTransaction = trans.RawTransaction;
                }
                ExportSignedTransaction signedEthTx
            | t when t = typeof<UtxoCoin.TransactionMetadata> ->
                let unsignedBtcTx = {
                    Metadata = box trans.TransactionInfo.Metadata :?> UtxoCoin.TransactionMetadata;
                    Proposal = trans.TransactionInfo.Proposal;
                    Cache = trans.TransactionInfo.Cache;
                }
                let signedBtcTx = {
                    TransactionInfo = unsignedBtcTx;
                    RawTransaction = trans.RawTransaction;
                }
                ExportSignedTransaction signedBtcTx
            | _ -> failwith "Unknown miner fee type"
        json

    let SerializeSignedTransaction (transaction: SignedTransaction<_>)
                                   (compressed: bool)
                                       : string =

        let json = SerializeSignedTransactionPlain transaction
        if not compressed then
            json
        else
            Marshalling.Compress json

    let SaveSignedTransaction (trans: SignedTransaction<_>) (filePath: string) =
        let json = SerializeSignedTransaction trans false
        File.WriteAllText(filePath, json)

    let AddPublicWatcher currency (publicAddress: string) =
        ValidateAddress currency publicAddress
        let readOnlyAccount = ReadOnlyAccount(currency, publicAddress)
        Config.AddReadonly readOnlyAccount

    let RemovePublicWatcher (account: ReadOnlyAccount) =
        Config.RemoveReadonly account

    let private CreateConceptEtherAccountInternal (password: string) (seed: array<byte>)
                                                 : Async<(string*string)*(FileInfo->string)> =
        async {
            let! fileName,encryptedPrivateKeyInJson = Ether.Account.Create password seed
            return (fileName,encryptedPrivateKeyInJson), Ether.Account.GetPublicAddressFromAccountFile
        }

    let private CreateConceptAccountInternal (currency: Currency) (password: string) (seed: array<byte>)
                                            : Async<(string*string)*(FileInfo->string)> =
        async {
            if currency.IsUtxo() then
                let! publicKey,encryptedPrivateKey = UtxoCoin.Account.Create currency password seed
                return (publicKey,encryptedPrivateKey), UtxoCoin.Account.GetPublicAddressFromAccountFile currency
            elif currency.IsEtherBased() then
                return! CreateConceptEtherAccountInternal password seed
            else
                return failwith (sprintf "Unknown currency %A" currency)
        }


    let CreateConceptAccount (currency: Currency) (password: string) (seed: array<byte>)
                            : Async<ConceptAccount> =
        async {
            let! (fileName, encryptedPrivateKey), fromEncPrivKeyToPubKeyFunc =
                CreateConceptAccountInternal currency password seed
            return {
                       Currency = currency;
                       FileNameAndContent = fileName,encryptedPrivateKey;
                       ExtractPublicKeyFromConfigFileFunc = fromEncPrivKeyToPubKeyFunc;
                   }
        }

    let private CreateConceptAccountAux (currency: Currency) (password: string) (seed: array<byte>)
                            : Async<List<ConceptAccount>> =
        async {
            let! singleAccount = CreateConceptAccount currency password seed
            return singleAccount::List.Empty
        }

    let CreateEtherNormalAccounts (password: string) (seed: array<byte>)
                                  : seq<Currency>*Async<List<ConceptAccount>> =
        let etherCurrencies = Currency.GetAll().Where(fun currency -> currency.IsEtherBased())
        let etherAccounts = async {
            let! (fileName, encryptedPrivateKey), fromEncPrivKeyToPubKeyFunc =
                CreateConceptEtherAccountInternal password seed
            return seq {
                for etherCurrency in etherCurrencies do
                    yield {
                              Currency = etherCurrency;
                              FileNameAndContent = fileName,encryptedPrivateKey;
                              ExtractPublicKeyFromConfigFileFunc = fromEncPrivKeyToPubKeyFunc;
                          }
            } |> List.ofSeq
        }
        etherCurrencies,etherAccounts

    let CreateNormalAccount (conceptAccount: ConceptAccount): NormalAccount =
        let fileName,fileContents = conceptAccount.FileNameAndContent
        let newAccountFile = Config.AddNormalAccount conceptAccount
        NormalAccount(conceptAccount.Currency, newAccountFile, conceptAccount.ExtractPublicKeyFromConfigFileFunc)

    let private LENGTH_OF_PRIVATE_KEYS = 32
    let CreateBaseAccount (passphrase: string)
                          (dobPartOfSalt: DateTime) (emailPartOfSalt: string)
                          (encryptionPassword: string): Async<unit> =

        let salt = sprintf "%s+%s" (dobPartOfSalt.Date.ToString("yyyyMMdd")) (emailPartOfSalt.ToLower())
        let privateKeyBytes = WarpKey.CreatePrivateKey passphrase salt

        let ethCurrencies,etherAccounts = CreateEtherNormalAccounts encryptionPassword privateKeyBytes
        let nonEthCurrencies = Currency.GetAll().Where(fun currency -> not (ethCurrencies.Contains currency))

        let nonEtherAccounts: List<Async<List<ConceptAccount>>> =
            seq {
                // TODO: figure out if we can reuse CPU computation of WIF creation between BTC&LTC
                for nonEthCurrency in nonEthCurrencies do
                    yield CreateConceptAccountAux nonEthCurrency encryptionPassword privateKeyBytes
            } |> List.ofSeq

        let allAccounts = etherAccounts::nonEtherAccounts

        let createAllAccountsJob = Async.Parallel allAccounts

        async {
            let! allCreatedConceptAccounts = createAllAccountsJob
            for accountGroup in allCreatedConceptAccounts do
                for conceptAccount in accountGroup do
                    CreateNormalAccount conceptAccount |> ignore
        }

    let public ExportUnsignedTransactionToJson trans =
        Marshalling.Serialize trans

    let private SerializeUnsignedTransactionPlain (transProposal: UnsignedTransactionProposal)
                                                  (txMetadata: IBlockchainFeeInfo)
                                                      : string =

        ValidateAddress transProposal.Amount.Currency transProposal.DestinationAddress

        let readOnlyAccounts = GetAllActiveAccounts().OfType<ReadOnlyAccount>()

        match txMetadata with
        | :? Ether.TransactionMetadata as etherTxMetadata ->
            Ether.Account.SaveUnsignedTransaction transProposal etherTxMetadata readOnlyAccounts
        | :? UtxoCoin.TransactionMetadata as btcTxMetadata ->
            UtxoCoin.Account.SaveUnsignedTransaction transProposal btcTxMetadata readOnlyAccounts
        | _ -> failwith "fee type unknown"

    let SerializeUnsignedTransaction (transProposal: UnsignedTransactionProposal)
                                     (txMetadata: IBlockchainFeeInfo)
                                     (compressed: bool)
                                         : string =

        let json = SerializeUnsignedTransactionPlain transProposal txMetadata
        if not compressed then
            json
        else
            Marshalling.Compress json

    let SaveUnsignedTransaction (transProposal: UnsignedTransactionProposal)
                                (txMetadata: IBlockchainFeeInfo)
                                (filePath: string) =
        let json = SerializeUnsignedTransaction transProposal txMetadata false
        File.WriteAllText(filePath, json)

    let public ImportUnsignedTransactionFromJson (jsonOrCompressedJson: string): UnsignedTransaction<IBlockchainFeeInfo> =

        let json =
            try
                Marshalling.Decompress jsonOrCompressedJson
            with
            | :? Marshalling.CompressionOrDecompressionException ->
                jsonOrCompressedJson

        let transType = Marshalling.ExtractType json

        match transType with
        | _ when transType = typeof<UnsignedTransaction<UtxoCoin.TransactionMetadata>> ->
            let deserializedBtcTransaction: UnsignedTransaction<UtxoCoin.TransactionMetadata> =
                    Marshalling.Deserialize json
            deserializedBtcTransaction.ToAbstract()
        | _ when transType = typeof<UnsignedTransaction<Ether.TransactionMetadata>> ->
            let deserializedBtcTransaction: UnsignedTransaction<Ether.TransactionMetadata> =
                    Marshalling.Deserialize json
            deserializedBtcTransaction.ToAbstract()
        | unexpectedType ->
            raise <| Exception(sprintf "Unknown unsignedTransaction subtype: %s" unexpectedType.FullName)

    let public ImportSignedTransactionFromJson (jsonOrCompressedJson: string): SignedTransaction<IBlockchainFeeInfo> =

        let json =
            try
                Marshalling.Decompress jsonOrCompressedJson
            with
            | :? Marshalling.CompressionOrDecompressionException ->
                jsonOrCompressedJson

        let transType = Marshalling.ExtractType json

        match transType with
        | _ when transType = typeof<SignedTransaction<UtxoCoin.TransactionMetadata>> ->
            let deserializedBtcTransaction: SignedTransaction<UtxoCoin.TransactionMetadata> =
                    Marshalling.Deserialize json
            deserializedBtcTransaction.ToAbstract()
        | _ when transType = typeof<SignedTransaction<Ether.TransactionMetadata>> ->
            let deserializedBtcTransaction: SignedTransaction<Ether.TransactionMetadata> =
                    Marshalling.Deserialize json
            deserializedBtcTransaction.ToAbstract()
        | unexpectedType ->
            raise <| Exception(sprintf "Unknown signedTransaction subtype: %s" unexpectedType.FullName)

    let public ImportTransactionFromJson (jsonOrCompressedJson: string): ImportedTransaction<IBlockchainFeeInfo> =

        let json =
            try
                Marshalling.Decompress jsonOrCompressedJson
            with
            | :? Marshalling.CompressionOrDecompressionException ->
                jsonOrCompressedJson

        let transType = Marshalling.ExtractType json

        match transType with
        | _ when transType = typeof<UnsignedTransaction<UtxoCoin.TransactionMetadata>> ->
            let deserializedTransaction: UnsignedTransaction<UtxoCoin.TransactionMetadata> =
                    Marshalling.Deserialize json
            Unsigned(deserializedTransaction.ToAbstract())
        | _ when transType = typeof<UnsignedTransaction<Ether.TransactionMetadata>> ->
            let deserializedTransaction: UnsignedTransaction<Ether.TransactionMetadata> =
                    Marshalling.Deserialize json
            Unsigned(deserializedTransaction.ToAbstract())
        | _ when transType = typeof<SignedTransaction<UtxoCoin.TransactionMetadata>> ->
            let deserializedTransaction: SignedTransaction<UtxoCoin.TransactionMetadata> =
                    Marshalling.Deserialize json
            Signed(deserializedTransaction.ToAbstract())
        | _ when transType = typeof<SignedTransaction<Ether.TransactionMetadata>> ->
            let deserializedTransaction: SignedTransaction<Ether.TransactionMetadata> =
                    Marshalling.Deserialize json
            Signed(deserializedTransaction.ToAbstract())
        | unexpectedType ->
            raise(new Exception(sprintf "Unknown unsignedTransaction subtype: %s" unexpectedType.FullName))


    let LoadSignedTransactionFromFile (filePath: string) =
        let signedTransInJson = File.ReadAllText(filePath)

        ImportSignedTransactionFromJson signedTransInJson

    let LoadUnsignedTransactionFromFile (filePath: string): UnsignedTransaction<IBlockchainFeeInfo> =
        let unsignedTransInJson = File.ReadAllText(filePath)

        ImportUnsignedTransactionFromJson unsignedTransInJson

