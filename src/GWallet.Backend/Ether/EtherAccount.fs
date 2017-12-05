namespace GWallet.Backend.Ether

// NOTE: we can rename this file to less redundant "Account.fs" when this F# compiler bug is fixed:
// https://github.com/Microsoft/visualfsharp/issues/3231

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

open GWallet.Backend

module internal Account =

    let private addressUtil = AddressUtil()
    let private signer = TransactionSigner()

    let private KeyStoreService = KeyStoreService()

    let GetPublicAddressFromUnencryptedPrivateKey (privateKey: string) =
        EthECKey(privateKey).GetPublicAddress()

    let GetPublicAddressFromAccountFile (accountFile: FileInfo) =
        let encryptedPrivateKey = File.ReadAllText(accountFile.FullName)
        let rawPublicAddress = KeyStoreService.GetAddressFromKeyStore encryptedPrivateKey
        let publicAddress =
            if (rawPublicAddress.StartsWith("0x")) then
                rawPublicAddress
            else
                "0x" + rawPublicAddress
        publicAddress

    // TODO: have a way to distinguish between confirmed and unconfirmed balance
    let GetBalance(account: IAccount): decimal =
        let balance = Ether.Server.GetBalance account.Currency account.PublicAddress
        UnitConversion.Convert.FromWei(balance.Value, UnitConversion.EthUnit.Ether)

    let ValidateAddress (currency: Currency) (address: string) =
        let ETHEREUM_ADDRESSES_LENGTH = 42
        let ETHEREUM_ADDRESS_PREFIX = "0x"

        if not (address.StartsWith(ETHEREUM_ADDRESS_PREFIX)) then
            raise (AddressMissingProperPrefix([ETHEREUM_ADDRESS_PREFIX]))

        if (address.Length <> ETHEREUM_ADDRESSES_LENGTH) then
            raise (AddressWithInvalidLength(ETHEREUM_ADDRESSES_LENGTH))

        if (not (addressUtil.IsChecksumAddress(address))) then
            let validCheckSumAddress = addressUtil.ConvertToChecksumAddress(address)
            raise (AddressWithInvalidChecksum(validCheckSumAddress))

    let EstimateFee (currency: Currency): MinerFee =
        let gasPrice = Ether.Server.GetGasPrice currency
        if (gasPrice.Value > BigInteger(Int64.MaxValue)) then
            failwith (sprintf "GWallet serialization doesn't support such a big integer (%s) for the gas, please report this issue."
                          (gasPrice.Value.ToString()))
        let gasPrice64: Int64 = BigInteger.op_Explicit gasPrice.Value
        MinerFee(gasPrice64, DateTime.Now, currency)

    let GetTransactionCount (currency: Currency) (publicAddress: string) =
        let result = (Ether.Server.GetTransactionCount currency publicAddress).Value
        if (result > BigInteger(Int64.MaxValue)) then
            failwith (sprintf "GWallet serialization doesn't support such a big integer (%s) for the nonce, please report this issue."
                          (result.ToString()))
        let int64result:Int64 = BigInteger.op_Explicit result
        int64result

    let private BroadcastRawTransaction (currency: Currency) trans =
        let insufficientFundsMsg = "Insufficient funds"
        try
            let txId = Ether.Server.BroadcastTransaction currency ("0x" + trans)
            txId
        with
        | ex when ex.Message.StartsWith(insufficientFundsMsg) || ex.InnerException.Message.StartsWith(insufficientFundsMsg) ->
            raise (InsufficientFunds)

    let BroadcastTransaction (trans: SignedTransaction<_>) =
        BroadcastRawTransaction
            trans.TransactionInfo.Proposal.Currency
            trans.RawTransaction

    let internal GetPrivateKey (account: NormalAccount) password =
        let encryptedPrivateKey = File.ReadAllText(account.AccountFile.FullName)
        let privKeyInBytes =
            try
                KeyStoreService.DecryptKeyStoreFromJson(password, encryptedPrivateKey)
            with
            | :? DecryptionException ->
                raise (InvalidPassword)

        EthECKey(privKeyInBytes, true)

    let private SignTransactionWithPrivateKey (account: IAccount)
                                              (txMetadata: TransactionMetadata)
                                              (destination: string)
                                              (amount: TransferAmount)
                                              (privateKey: EthECKey) =

        let currency = account.Currency
        if (txMetadata.Fee.Currency <> currency) then
            invalidArg "account" "currency of account param must be equal to currency of minerFee param"

        let amountToSendConsideringMinerFee =
            if (amount.IdealValueRemainingAfterSending = 0.0m) then
                amount.ValueToSend - (txMetadata :> IBlockchainFeeInfo).FeeValue
            else
                amount.ValueToSend
        let amountInWei = UnitConversion.Convert.ToWei(amountToSendConsideringMinerFee,
                                                       UnitConversion.EthUnit.Ether)

        let privKeyInBytes = privateKey.GetPrivateKeyAsBytes()
        let trans = signer.SignTransaction(
                        privKeyInBytes,
                        destination,
                        amountInWei,
                        BigInteger(txMetadata.TransactionCount),

                        // we use the SignTransaction() overload that has these 2 arguments because if we don't, we depend on
                        // how well the defaults are of Geth node we're connected to, e.g. with the myEtherWallet server I
                        // was trying to spend 0.002ETH from an account that had 0.01ETH and it was always failing with the
                        // "Insufficient Funds" error saying it needed 212,000,000,000,000,000 wei (0.212 ETH)...
                        BigInteger(txMetadata.Fee.GasPriceInWei),
                        MinerFee.GAS_COST_FOR_A_NORMAL_ETHER_TRANSACTION)

        if not (signer.VerifyTransaction(trans)) then
            failwith "Transaction could not be verified?"
        trans

    let SignTransaction (account: NormalAccount)
                        (txMetadata: TransactionMetadata)
                        (destination: string)
                        (amount: TransferAmount)
                        (password: string) =

        let privateKey = GetPrivateKey account password
        SignTransactionWithPrivateKey account txMetadata destination amount privateKey

    let SweepArchivedFunds (account: ArchivedAccount)
                           (balance: decimal)
                           (destination: IAccount)
                           (txMetadata: TransactionMetadata) =
        let accountFrom = (account:>IAccount)
        let amount = TransferAmount(balance, 0.0m)
        let ecPrivKey = EthECKey(account.PrivateKey)
        let signedTrans = SignTransactionWithPrivateKey
                              account txMetadata destination.PublicAddress amount ecPrivKey
        BroadcastRawTransaction accountFrom.Currency signedTrans

    let SendPayment (account: NormalAccount)
                    (txMetadata: TransactionMetadata)
                    (destination: string)
                    (amount: TransferAmount)
                    (password: string) =
        let baseAccount = account :> IAccount
        if (baseAccount.PublicAddress.Equals(destination, StringComparison.InvariantCultureIgnoreCase)) then
            raise DestinationEqualToOrigin

        let currency = baseAccount.Currency

        let trans = SignTransaction account txMetadata destination amount password

        BroadcastRawTransaction currency trans

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
        if not (addressUtil.IsChecksumAddress(publicAddress)) then
            failwith ("Nethereum's GetPublicAddress gave a non-checksum address: " + publicAddress)

        let accountSerializedJson =
            KeyStoreService.EncryptAndGenerateDefaultKeyStoreAsJson(password,
                                                                    privateKeyTrimmed,
                                                                    publicAddress)
        let fileNameForAccount = KeyStoreService.GenerateUTCFileName(publicAddress)
        fileNameForAccount,accountSerializedJson

    let public ExportUnsignedTransactionToJson trans =
        Marshalling.Serialize trans

    let SaveUnsignedTransaction (transProposal: UnsignedTransactionProposal)
                                (txMetadata: TransactionMetadata)
                                (filePath: string)
                                =

        let unsignedTransaction =
            {
                Proposal = transProposal;
                Cache = Caching.GetLastCachedData();
                Metadata = txMetadata;
            }
        let json = ExportUnsignedTransactionToJson unsignedTransaction
        File.WriteAllText(filePath, json)

