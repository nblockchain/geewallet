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


    let EstimateFee account amount: IBlockchainFee =
        let currency = (account:>IAccount).Currency
        match currency with
        | Currency.BTC ->
            Bitcoin.Account.EstimateFee account amount :> IBlockchainFee
        | Currency.ETH | Currency.ETC ->
            Ether.Account.EstimateFee currency :> IBlockchainFee

    let BroadcastTransaction (trans: SignedTransaction) =
        Ether.Account.BroadcastTransaction trans

    let SignTransaction (account: NormalAccount)
                        (transCount: BigInteger)
                        (destination: string)
                        (amount: decimal)
                        (minerFee: IBlockchainFee)
                        (password: string) =

        match minerFee with
        | :? EtherMinerFee as etherMinerFee ->
            Ether.Account.SignTransaction
                  account
                  transCount
                  destination
                  amount
                  etherMinerFee
                  password
        | _ -> failwith "fee type unknown"

    let Archive (account: NormalAccount)
                (password: string) =
        let privateKey = Ether.Account.GetPrivateKey account password
        let newArchivedAccount = ArchivedAccount((account:>IAccount).Currency, privateKey)
        Config.AddArchived newArchivedAccount
        Config.RemoveNormal account

    let SweepArchivedFunds (account: ArchivedAccount)
                           (balance: decimal)
                           (destination: IAccount)
                           (fee: IBlockchainFee) =
        match fee with
        | :? EtherMinerFee as etherMinerFee -> Ether.Account.SweepArchivedFunds account balance destination etherMinerFee
        | _ -> failwith "fee type unknown"

    let SendPayment (account: NormalAccount) (destination: string) (amount: decimal)
                    (password: string) (minerFee: IBlockchainFee) =
        let baseAccount = account :> IAccount
        if (baseAccount.PublicAddress.Equals(destination, StringComparison.InvariantCultureIgnoreCase)) then
            raise DestinationEqualToOrigin

        ValidateAddress baseAccount.Currency destination

        let currency = (account:>IAccount).Currency
        match currency with
        | Currency.BTC ->
            match minerFee with
            | :? Bitcoin.MinerFee as bitcoinMinerFee ->
                Bitcoin.Account.SendPayment account destination amount password bitcoinMinerFee
            | _ -> failwith "fee for BTC currency should be Bitcoin.MinerFee type"
        | Currency.ETH | Currency.ETC ->
            match minerFee with
            | :? EtherMinerFee as etherMinerFee ->
                Ether.Account.SendPayment account destination amount password etherMinerFee
            | _ -> failwith "fee for Ether currency should be EtherMinerFee type"

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
        let newAccountFile = Config.AddNormalAccount currency fileName encryptedPrivateKey
        NormalAccount(currency, newAccountFile, fromEncPrivKeyToPubKeyFunc)

    let public ExportUnsignedTransactionToJson trans =
        Marshalling.Serialize trans

    let SaveUnsignedTransaction (transProposal: UnsignedTransactionProposal)
                                (fee: IBlockchainFee)
                                (filePath: string) =

        ValidateAddress transProposal.Currency transProposal.DestinationAddress

        match fee with
        | :? EtherMinerFee as etherMinerFee -> Ether.Account.SaveUnsignedTransaction transProposal etherMinerFee filePath
        | _ -> failwith "fee type unknown"

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

