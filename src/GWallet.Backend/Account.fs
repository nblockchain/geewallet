namespace GWallet.Backend

open System
open System.Net
open System.Linq
open System.Numerics
open System.IO

open Newtonsoft.Json
open Nethereum.Signer

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
                for accountFile in Config.GetAllArchivedAccounts(currency) do
                    let privKey = File.ReadAllText(accountFile.FullName)
                    let ecPrivKey = EthECKey(privKey)
                    let account = ArchivedAccount(currency, ecPrivKey)

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


    let EstimateFee (currency: Currency): EtherMinerFee =
        Ether.Account.EstimateFee currency

    let BroadcastTransaction (trans: SignedTransaction) =
        Ether.Account.BroadcastTransaction trans

    let SignTransaction (account: NormalAccount)
                        (transCount: BigInteger)
                        (destination: string)
                        (amount: decimal)
                        (minerFee: EtherMinerFee)
                        (password: string) =

        Ether.Account.SignTransaction
                          account
                          transCount
                          destination
                          amount
                          minerFee
                          password

    let Archive (account: NormalAccount)
                (password: string) =
        let privateKey = Ether.Account.GetPrivateKey account password
        let newArchivedAccount = ArchivedAccount((account:>IAccount).Currency, privateKey)
        Config.AddArchived newArchivedAccount
        Config.RemoveNormal account

    let SweepArchivedFunds (account: ArchivedAccount)
                           (balance: decimal)
                           (destination: IAccount)
                           (minerFee: EtherMinerFee) =

        Ether.Account.SweepArchivedFunds
                          account
                          balance
                          destination
                          minerFee

    let SendPayment (account: NormalAccount) (destination: string) (amount: decimal)
                    (password: string) (minerFee: EtherMinerFee) =
        let baseAccount = account :> IAccount
        if (baseAccount.PublicAddress.Equals(destination, StringComparison.InvariantCultureIgnoreCase)) then
            raise DestinationEqualToOrigin

        ValidateAddress baseAccount.Currency destination

        Ether.Account.SendPayment account destination amount password minerFee

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
        ValidateAddress currency publicAddress
        let readOnlyAccount = ReadOnlyAccount(currency, publicAddress)
        Config.AddReadonly readOnlyAccount

    let RemovePublicWatcher (account: ReadOnlyAccount) =
        Config.RemoveReadonly account

    let Create (currency: Currency) (password: string): NormalAccount =
        let (fileName, encryptedPrivateKey), fromEncPrivKeyToPubKeyFunc =
            match currency with
            | Currency.BTC -> Bitcoin.Account.Create password, Bitcoin.Account.GetPublicAddressFromAccountFile
            | Currency.ETH | Currency.ETC ->
                Ether.Account.Create currency password, Ether.Account.GetPublicAddressFromAccountFile
            | _ -> failwith (sprintf "Unknown currency %A" currency)
        let newAccountFile = Config.AddNormalAccount currency fileName encryptedPrivateKey
        NormalAccount(currency, newAccountFile, fromEncPrivKeyToPubKeyFunc)

    let public ExportUnsignedTransactionToJson trans =
        Marshalling.Serialize trans

    let SaveUnsignedTransaction (transProposal: UnsignedTransactionProposal)
                                (fee: EtherMinerFee)
                                (filePath: string) =

        ValidateAddress transProposal.Currency transProposal.DestinationAddress

        Ether.Account.SaveUnsignedTransaction transProposal fee filePath

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

