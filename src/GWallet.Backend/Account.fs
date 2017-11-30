namespace GWallet.Backend

open System
open System.Net
open System.Linq
open System.Numerics
open System.IO

open Newtonsoft.Json

module Account =

    let GetBalance(account: IAccount): MaybeCached<decimal> =
        match account.Currency with
        | Currency.ETH | Currency.ETC ->
            Ether.Account.GetBalance account
        | Currency.BTC ->
            Fresh(Bitcoin.Account.GetBalance account)

    let GetAllActiveAccounts(): seq<IAccount> =
        seq {
            let allCurrencies = Currency.GetAll()

            for currency in allCurrencies do

                for accountFile in Config.GetAllReadOnlyAccounts(currency) do
                    let fileName = Path.GetFileName(accountFile.FullName)
                    yield ReadOnlyAccount(currency, fileName) :> IAccount

                let fromAccountFileToPublicAddress =
                    match currency with
                    | Currency.BTC -> Bitcoin.Account.GetPublicAddressFromAccountFile
                    | Currency.ETH | Currency.ETC -> Ether.Account.GetPublicAddressFromAccountFile
                    | _ -> failwith (sprintf "Unknown currency %A" currency)
                for accountFile in Config.GetAllNormalAccounts(currency) do
                    yield NormalAccount(currency, accountFile, fromAccountFileToPublicAddress) :> IAccount
        }

    let GetArchivedAccountsWithPositiveBalance(): seq<ArchivedAccount*decimal> =
        seq {
            let allCurrencies = Currency.GetAll()

            for currency in allCurrencies do
                let fromAccountFileToPublicAddress =
                    match currency with
                    | Currency.BTC ->
                        Bitcoin.Account.GetPublicAddressFromUnencryptedPrivateKey
                    | Currency.ETH | Currency.ETC ->
                        Ether.Account.GetPublicAddressFromUnencryptedPrivateKey
                    | _ -> failwith (sprintf "Unknown currency %A" currency)

                for accountFile in Config.GetAllArchivedAccounts(currency) do
                    let account = ArchivedAccount(currency, accountFile, fromAccountFileToPublicAddress)

                    match GetBalance(account) with
                    | NotFresh(NotAvailable) -> ()
                    | Fresh(balance) ->
                        if (balance > 0m) then
                            yield account,balance
                    | NotFresh(Cached(balance,time)) ->
                        () // TODO: do something in this case??
        }

    let ValidateAddress (currency: Currency) (address: string) =
        match currency with
        | Currency.ETH | Currency.ETC ->
            Ether.Account.ValidateAddress currency address

        | Currency.BTC ->
            Bitcoin.Account.ValidateAddress address

            // FIXME: add bitcoin checksum algorithm?
        ()


    let EstimateFee account amount destination: IBlockchainFeeInfo =
        let currency = (account:>IAccount).Currency
        match currency with
        | Currency.BTC ->
            Bitcoin.Account.EstimateFee account amount destination :> IBlockchainFeeInfo
        | Currency.ETH | Currency.ETC ->
            let ethMinerFee = Ether.Account.EstimateFee currency
            let txCount = Ether.Account.GetTransactionCount account.Currency account.PublicAddress
            { Ether.Fee = ethMinerFee; Ether.TransactionCount = txCount } :> IBlockchainFeeInfo

    let BroadcastTransaction (trans: SignedTransaction<_>) =
        match trans.TransactionInfo.Proposal.Currency with
        | Currency.ETH | Currency.ETC ->
            Ether.Account.BroadcastTransaction trans
        | Currency.BTC ->
            Bitcoin.Account.BroadcastTransaction trans
        | _ -> failwith "fee type unknown"

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
        | :? Bitcoin.TransactionMetadata as btcTxMetadata ->
            Bitcoin.Account.SignTransaction
                account
                btcTxMetadata
                destination
                amount
                password
        | _ -> failwith "fee type unknown"

    let private CreateArchivedAccount (currency: Currency) (unencryptedPrivateKey: string): ArchivedAccount =
        let fromUnencryptedPrivateKeyToPublicAddressFunc =
            match currency with
            | Currency.BTC ->
                Bitcoin.Account.GetPublicAddressFromUnencryptedPrivateKey
            | Currency.ETH | Currency.ETC ->
                Ether.Account.GetPublicAddressFromUnencryptedPrivateKey
        let fileName = fromUnencryptedPrivateKeyToPublicAddressFunc unencryptedPrivateKey
        let newAccountFile = Config.AddArchivedAccount currency fileName unencryptedPrivateKey
        ArchivedAccount(currency, newAccountFile, fromUnencryptedPrivateKeyToPublicAddressFunc)

    let Archive (account: NormalAccount)
                (password: string)
                : unit =
        let currency = (account:>IAccount).Currency
        let privateKeyAsString =
            match currency with
            | Currency.BTC ->
                let privKey = Bitcoin.Account.GetPrivateKey account password
                privKey.GetWif(Config.BitcoinNet).ToWif()
            | Currency.ETC | Currency.ETH ->
                let privKey = Ether.Account.GetPrivateKey account password
                privKey.GetPrivateKey()
        CreateArchivedAccount currency privateKeyAsString |> ignore
        Config.RemoveNormal account

    let SweepArchivedFunds (account: ArchivedAccount)
                           (balance: decimal)
                           (destination: IAccount)
                           (txMetadata: IBlockchainFeeInfo) =
        match txMetadata with
        | :? Ether.TransactionMetadata as etherTxMetadata ->
            Ether.Account.SweepArchivedFunds account balance destination etherTxMetadata
        | :? Bitcoin.TransactionMetadata as btcTxMetadata ->
            Bitcoin.Account.SweepArchivedFunds account balance destination btcTxMetadata
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
        match currency with
        | Currency.BTC ->
            match txMetadata with
            | :? Bitcoin.TransactionMetadata as btcTxMetadata ->
                Bitcoin.Account.SendPayment account btcTxMetadata destination amount password
            | _ -> failwith "fee for BTC currency should be Bitcoin.MinerFee type"
        | Currency.ETH | Currency.ETC ->
            match txMetadata with
            | :? Ether.TransactionMetadata as etherTxMetadata ->
                Ether.Account.SendPayment account etherTxMetadata destination amount password
            | _ -> failwith "fee for Ether currency should be EtherMinerFee type"

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
            | t when t = typeof<Bitcoin.TransactionMetadata> ->
                let unsignedBtcTx = {
                    Metadata = box trans.TransactionInfo.Metadata :?> Bitcoin.TransactionMetadata;
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

    let CreateNormalAccount (currency: Currency) (password: string): NormalAccount =
        let (fileName, encryptedPrivateKey), fromEncPrivKeyToPubKeyFunc =
            match currency with
            | Currency.BTC ->
                let publicKey,encryptedPrivateKey = Bitcoin.Account.Create password
                (publicKey,encryptedPrivateKey), Bitcoin.Account.GetPublicAddressFromAccountFile
            | Currency.ETH | Currency.ETC ->
                let fileName,encryptedPrivateKeyInJson = Ether.Account.Create currency password
                (fileName,encryptedPrivateKeyInJson), Ether.Account.GetPublicAddressFromAccountFile
        let newAccountFile = Config.AddNormalAccount currency fileName encryptedPrivateKey
        NormalAccount(currency, newAccountFile, fromEncPrivKeyToPubKeyFunc)

    let public ExportUnsignedTransactionToJson trans =
        Marshalling.Serialize trans

    let SaveUnsignedTransaction (transProposal: UnsignedTransactionProposal)
                                (txMetadata: IBlockchainFeeInfo)
                                (filePath: string) =

        ValidateAddress transProposal.Currency transProposal.DestinationAddress

        match txMetadata with
        | :? Ether.TransactionMetadata as etherTxMetadata ->
            Ether.Account.SaveUnsignedTransaction transProposal etherTxMetadata filePath
        | :? Bitcoin.TransactionMetadata as btcTxMetadata ->
            Bitcoin.Account.SaveUnsignedTransaction transProposal btcTxMetadata filePath
        | _ -> failwith "fee type unknown"

    let public ImportUnsignedTransactionFromJson (json: string): UnsignedTransaction<IBlockchainFeeInfo> =

        let transType = Marshalling.ExtractType json

        match transType with
        | _ when transType = typeof<UnsignedTransaction<Bitcoin.TransactionMetadata>> ->
            let deserializedBtcTransaction: UnsignedTransaction<Bitcoin.TransactionMetadata> =
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
        | _ when transType = typeof<SignedTransaction<Bitcoin.TransactionMetadata>> ->
            let deserializedBtcTransaction: SignedTransaction<Bitcoin.TransactionMetadata> =
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

