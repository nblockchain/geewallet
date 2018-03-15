namespace GWallet.Backend

open System
open System.Net
open System.Linq
open System.Numerics
open System.IO

open Org.BouncyCastle.Security
open Newtonsoft.Json

module Account =

    let private GetBalanceFromServerOrCache(account: IAccount) (onlyConfirmed: bool): MaybeCached<decimal> =
        let maybeBalance =
            try
                if account.Currency.IsEtherBased() then
                    if (onlyConfirmed) then
                        Some(Ether.Account.GetConfirmedBalance account)
                    else
                        Some(Ether.Account.GetUnconfirmedPlusConfirmedBalance account)
                elif (account.Currency.IsUtxo()) then
                    if (onlyConfirmed) then
                        Some(UtxoCoin.Account.GetConfirmedBalance account)
                    else
                        Some(UtxoCoin.Account.GetUnconfirmedPlusConfirmedBalance account)
                else
                    failwith (sprintf "Unknown currency %A" account.Currency)
            with
            | :? NoneAvailableException as ex -> None

        match maybeBalance with
        | None ->
            NotFresh(Caching.RetreiveLastBalance(account.PublicAddress, account.Currency))
        | Some(balance) ->
            Caching.StoreLastBalance (account.PublicAddress, account.Currency) balance
            Fresh(balance)

    let GetUnconfirmedPlusConfirmedBalance(account: IAccount) =
        GetBalanceFromServerOrCache account false

    let GetConfirmedBalance(account: IAccount) =
        GetBalanceFromServerOrCache account true

    let GetShowableBalance(account: IAccount) =
        let unconfirmed = GetUnconfirmedPlusConfirmedBalance account
        let confirmed = GetConfirmedBalance account
        match unconfirmed,confirmed with
        | Fresh(unconfirmedAmount),Fresh(confirmedAmount) ->
            if (unconfirmedAmount < confirmedAmount) then
                unconfirmed
            else
                confirmed
        | _ -> confirmed

    let GetAllActiveAccounts(): seq<IAccount> =
        seq {
            let allCurrencies = Currency.GetAll()

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
        }

    let GetArchivedAccountsWithPositiveBalance(): seq<ArchivedAccount*decimal> =
        seq {
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

                    match GetUnconfirmedPlusConfirmedBalance(account) with
                    | NotFresh(NotAvailable) -> ()
                    | Fresh(balance) ->
                        if (balance > 0m) then
                            yield account,balance
                    | NotFresh(Cached(balance,time)) ->
                        () // TODO: do something in this case??
        }

    // TODO: add tests for these (just in case address validation breaks after upgrading our dependencies)
    let ValidateAddress (currency: Currency) (address: string) =
        if currency.IsEtherBased() then
            Ether.Account.ValidateAddress currency address
        elif currency.IsUtxo() then
            UtxoCoin.Account.ValidateAddress currency address
        else
            failwith (sprintf "Unknown currency %A" currency)

    let EstimateFee account amount destination: IBlockchainFeeInfo =
        let currency = (account:>IAccount).Currency
        if currency.IsUtxo() then
            UtxoCoin.Account.EstimateFee account amount destination :> IBlockchainFeeInfo
        elif currency.IsEtherBased() then
            Ether.Account.EstimateFee account amount destination :> IBlockchainFeeInfo
        else
            failwith (sprintf "Unknown currency %A" currency)

    let BroadcastTransaction (trans: SignedTransaction<_>) =
        let currency = trans.TransactionInfo.Proposal.Currency
        if currency.IsEtherBased() then
            Ether.Account.BroadcastTransaction trans
        elif currency.IsUtxo() then
            UtxoCoin.Account.BroadcastTransaction currency trans
        else
            failwith (sprintf "Unknown currency %A" currency)

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

        ValidateAddress baseAccount.Currency destination

        let currency = (account:>IAccount).Currency
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

    let SignUnsignedTransaction (account)
                                (unsignedTrans: UnsignedTransaction<IBlockchainFeeInfo>)
                                password =
        let rawTransaction = SignTransaction account
                                 unsignedTrans.Proposal.DestinationAddress
                                 unsignedTrans.Proposal.Amount
                                 unsignedTrans.Metadata
                                 password

        { TransactionInfo = unsignedTrans; RawTransaction = rawTransaction }

    let public ExportSignedTransaction (trans: SignedTransaction<_>) =
        Marshalling.Serialize trans

    let SaveSignedTransaction (trans: SignedTransaction<_>) (filePath: string) =

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

        File.WriteAllText(filePath, json)

    let AddPublicWatcher currency (publicAddress: string) =
        ValidateAddress currency publicAddress
        let readOnlyAccount = ReadOnlyAccount(currency, publicAddress)
        Config.AddReadonly readOnlyAccount

    let RemovePublicWatcher (account: ReadOnlyAccount) =
        Config.RemoveReadonly account

    let CreateNormalAccount (currency: Currency) (password: string) (seed: Option<array<byte>>): NormalAccount =
        let (fileName, encryptedPrivateKey), fromEncPrivKeyToPubKeyFunc =
            if currency.IsUtxo() then
                let publicKey,encryptedPrivateKey = UtxoCoin.Account.Create currency password seed
                (publicKey,encryptedPrivateKey), UtxoCoin.Account.GetPublicAddressFromAccountFile currency
            elif currency.IsEtherBased() then
                let fileName,encryptedPrivateKeyInJson = Ether.Account.Create currency password seed
                (fileName,encryptedPrivateKeyInJson), Ether.Account.GetPublicAddressFromAccountFile
            else
                failwith (sprintf "Unknown currency %A" currency)
        let newAccountFile = Config.AddNormalAccount currency fileName encryptedPrivateKey
        NormalAccount(currency, newAccountFile, fromEncPrivKeyToPubKeyFunc)

    let private LENGTH_OF_PRIVATE_KEYS = 32
    let CreateBaseAccount (password: string) : list<NormalAccount> =
        let privateKeyBytes = Array.zeroCreate LENGTH_OF_PRIVATE_KEYS
        SecureRandom().NextBytes(privateKeyBytes)
        seq {
            let allCurrencies = Currency.GetAll()

            for currency in allCurrencies do
                yield CreateNormalAccount currency password (Some(privateKeyBytes))
        } |> List.ofSeq

    let public ExportUnsignedTransactionToJson trans =
        Marshalling.Serialize trans

    let SaveUnsignedTransaction (transProposal: UnsignedTransactionProposal)
                                (txMetadata: IBlockchainFeeInfo)
                                (filePath: string) =

        ValidateAddress transProposal.Currency transProposal.DestinationAddress

        match txMetadata with
        | :? Ether.TransactionMetadata as etherTxMetadata ->
            Ether.Account.SaveUnsignedTransaction transProposal etherTxMetadata filePath
        | :? UtxoCoin.TransactionMetadata as btcTxMetadata ->
            UtxoCoin.Account.SaveUnsignedTransaction transProposal btcTxMetadata filePath
        | _ -> failwith "fee type unknown"

    let public ImportUnsignedTransactionFromJson (json: string): UnsignedTransaction<IBlockchainFeeInfo> =

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
            raise(new Exception(sprintf "Unknown unsignedTransaction subtype: %s" unexpectedType.FullName))

    let public ImportSignedTransactionFromJson (json: string): SignedTransaction<IBlockchainFeeInfo> =
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
            raise(new Exception(sprintf "Unknown signedTransaction subtype: %s" unexpectedType.FullName))

    let LoadSignedTransactionFromFile (filePath: string) =
        let signedTransInJson = File.ReadAllText(filePath)

        ImportSignedTransactionFromJson signedTransInJson

    let LoadUnsignedTransactionFromFile (filePath: string): UnsignedTransaction<IBlockchainFeeInfo> =
        let unsignedTransInJson = File.ReadAllText(filePath)

        ImportUnsignedTransactionFromJson unsignedTransInJson

