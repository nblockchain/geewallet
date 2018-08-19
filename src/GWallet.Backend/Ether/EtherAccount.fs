namespace GWallet.Backend.Ether

// NOTE: we can rename this file to less redundant "Account.fs" when this F# compiler bug is fixed:
// https://github.com/Microsoft/visualfsharp/issues/3231

open System
open System.Numerics
open System.IO

open Nethereum.Signer
open Nethereum.KeyStore
open Nethereum.Util
open Nethereum.KeyStore.Crypto

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

    let GetConfirmedBalance(account: IAccount): Async<decimal> = async {
        if (account.Currency.IsEther()) then
            let! etherBalance = Ether.Server.GetConfirmedEtherBalance account.Currency account.PublicAddress
            return UnitConversion.Convert.FromWei(etherBalance.Value, UnitConversion.EthUnit.Ether)
        elif (account.Currency.IsEthToken()) then
            let! tokenBalance = Ether.Server.GetConfirmedTokenBalance account.Currency account.PublicAddress
            return UnitConversion.Convert.FromWei(tokenBalance, UnitConversion.EthUnit.Ether)
        else
            return failwithf "Assertion failed: currency %A should be Ether or Ether token" account.Currency
        }

    let GetUnconfirmedPlusConfirmedBalance(account: IAccount): Async<decimal> = async {
        if (account.Currency.IsEther()) then
            let! etherBalance = Ether.Server.GetUnconfirmedEtherBalance account.Currency account.PublicAddress
            return UnitConversion.Convert.FromWei(etherBalance.Value, UnitConversion.EthUnit.Ether)
        elif (account.Currency.IsEthToken()) then
            let! tokenBalance = Ether.Server.GetUnconfirmedTokenBalance account.Currency account.PublicAddress
            return UnitConversion.Convert.FromWei(tokenBalance, UnitConversion.EthUnit.Ether)
        else
            return failwithf "Assertion failed: currency %A should be Ether or Ether token" account.Currency
        }

    let ValidateAddress (currency: Currency) (address: string) =
        let ETHEREUM_ADDRESSES_LENGTH = 42
        let ETHEREUM_ADDRESS_PREFIX = "0x"

        if not (address.StartsWith(ETHEREUM_ADDRESS_PREFIX)) then
            raise (AddressMissingProperPrefix([ETHEREUM_ADDRESS_PREFIX]))

        if (address.Length <> ETHEREUM_ADDRESSES_LENGTH) then
            raise (AddressWithInvalidLength(ETHEREUM_ADDRESSES_LENGTH))

        if (not (addressUtil.IsChecksumAddress(address))) then
            let validCheckSumAddress = addressUtil.ConvertToChecksumAddress(address)
            raise (AddressWithInvalidChecksum(Some validCheckSumAddress))

    let private GetTransactionCount (currency: Currency) (publicAddress: string): Async<int64> = async {
        let! result = Ether.Server.GetTransactionCount currency publicAddress
        let value = result.Value
        if (value > BigInteger(Int64.MaxValue)) then
            failwith (sprintf "GWallet serialization doesn't support such a big integer (%s) for the nonce, please report this issue."
                          (result.ToString()))
        let int64result:Int64 = BigInteger.op_Explicit value
        return int64result
        }

    let private GetGasPrice currency: Async<int64> = async {
        let! gasPrice = Ether.Server.GetGasPrice currency
        if (gasPrice.Value > BigInteger(Int64.MaxValue)) then
            failwith (sprintf "GWallet serialization doesn't support such a big integer (%s) for the gas, please report this issue."
                          (gasPrice.Value.ToString()))
        let gasPrice64: Int64 = BigInteger.op_Explicit gasPrice.Value
        return gasPrice64
        }

    let private GAS_COST_FOR_A_NORMAL_ETHER_TRANSACTION:int64 = 21000L

    let EstimateEtherTransferFee (account: IAccount) (amount: TransferAmount): Async<TransactionMetadata> = async {
        let! gasPrice64 = GetGasPrice account.Currency
        let ethMinerFee = MinerFee(GAS_COST_FOR_A_NORMAL_ETHER_TRANSACTION, gasPrice64, DateTime.Now, account.Currency)
        let! txCount = GetTransactionCount account.Currency account.PublicAddress

        let feeValue = ethMinerFee.CalculateAbsoluteValue()
        if (amount.ValueToSend <> amount.BalanceAtTheMomentOfSending &&
            feeValue > (amount.BalanceAtTheMomentOfSending - amount.ValueToSend)) then
            raise (InsufficientBalanceForFee feeValue)

        return { Ether.Fee = ethMinerFee; Ether.TransactionCount = txCount }
    }

    // FIXME: this should raise InsufficientBalanceForFee
    let EstimateTokenTransferFee (account: IAccount) amount destination: Async<TransactionMetadata> = async {
        let! gasPrice64 = GetGasPrice account.Currency

        let! tokenTransferFee = Ether.Server.EstimateTokenTransferFee account amount destination
        if (tokenTransferFee.Value > BigInteger(Int64.MaxValue)) then
            failwith (sprintf "GWallet serialization doesn't support such a big integer (%s) for the gas cost of the token transfer, please report this issue."
                          (tokenTransferFee.Value.ToString()))
        let gasCost64: Int64 = BigInteger.op_Explicit tokenTransferFee.Value
        let baseCurrency =
            match account.Currency with
            | DAI -> ETH
            | _ -> failwithf "Unknown token %A" account.Currency
        let ethMinerFee = MinerFee(gasCost64, gasPrice64, DateTime.Now, baseCurrency)
        let! txCount = GetTransactionCount account.Currency account.PublicAddress
        return { Ether.Fee = ethMinerFee; Ether.TransactionCount = txCount }
        }

    let EstimateFee (account: IAccount) (amount: TransferAmount) destination: Async<TransactionMetadata> = async {
        if account.Currency.IsEther() then
            return! EstimateEtherTransferFee account amount
        elif account.Currency.IsEthToken() then
            return! EstimateTokenTransferFee account amount.ValueToSend destination
        else
            return failwithf "Assertion failed: currency %A should be Ether or Ether token" account.Currency
        }

    let private BroadcastRawTransaction (currency: Currency) trans =
        Ether.Server.BroadcastTransaction currency ("0x" + trans)

    let BroadcastTransaction (trans: SignedTransaction<_>) =
        BroadcastRawTransaction
            trans.TransactionInfo.Proposal.Amount.Currency
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

    let private GetNetwork (currency: Currency) =
        if not (currency.IsEtherBased()) then
            failwithf "Assertion failed: currency %A should be Ether-type" currency
        if currency.IsEthToken() || currency = ETH then
            Config.EthNet
        elif currency = ETC then
            Config.EtcNet
        else
            failwithf "Assertion failed: Ether currency %A not supported?" currency

    let private SignEtherTransaction (chain: Chain)
                                     (txMetadata: TransactionMetadata)
                                     (destination: string)
                                     (amount: TransferAmount)
                                     (privateKey: EthECKey) =

        if (GetNetwork txMetadata.Fee.Currency <> chain) then
            invalidArg "chain" (sprintf "Assertion failed: fee currency (%A) chain doesn't match with passed chain (%A)"
                                        txMetadata.Fee.Currency chain)

        let amountToSendConsideringMinerFee =
            if (amount.ValueToSend = amount.BalanceAtTheMomentOfSending) then
                amount.ValueToSend - (txMetadata :> IBlockchainFeeInfo).FeeValue
            else
                amount.ValueToSend
        let amountInWei = UnitConversion.Convert.ToWei(amountToSendConsideringMinerFee,
                                                       UnitConversion.EthUnit.Ether)

        let privKeyInBytes = privateKey.GetPrivateKeyAsBytes()
        let trans = signer.SignTransaction(
                        privKeyInBytes,
                        chain,
                        destination,
                        amountInWei,
                        BigInteger(txMetadata.TransactionCount),

                        // we use the SignTransaction() overload that has these 2 arguments because if we don't, we depend on
                        // how well the defaults are of Geth node we're connected to, e.g. with the myEtherWallet server I
                        // was trying to spend 0.002ETH from an account that had 0.01ETH and it was always failing with the
                        // "Insufficient Funds" error saying it needed 212,000,000,000,000,000 wei (0.212 ETH)...
                        BigInteger(txMetadata.Fee.GasPriceInWei),
                        BigInteger(txMetadata.Fee.GasLimit))
        trans

    let private SignEtherTokenTransaction (chain: Chain)
                                          (txMetadata: TransactionMetadata)
                                          (origin: string)
                                          (destination: string)
                                          (amount: TransferAmount)
                                          (privateKey: EthECKey) =

        let privKeyInBytes = privateKey.GetPrivateKeyAsBytes()

        let amountInWei = UnitConversion.Convert.ToWei(amount.ValueToSend, UnitConversion.EthUnit.Ether)
        let gasLimit = BigInteger(txMetadata.Fee.GasLimit)
        let data = TokenManager.OfflineDaiContract.ComposeInputDataForTransferTransaction(origin,
                                                                                          destination,
                                                                                          amountInWei,
                                                                                          gasLimit)

        let etherValue = BigInteger(0)
        let nonce = BigInteger(txMetadata.TransactionCount)
        let gasPrice = BigInteger(txMetadata.Fee.GasPriceInWei)

        signer.SignTransaction (privKeyInBytes,
                                chain,
                                TokenManager.DAI_CONTRACT_ADDRESS,
                                etherValue,
                                nonce,
                                gasPrice,
                                gasLimit,
                                data)

    let private SignTransactionWithPrivateKey (account: IAccount)
                                              (txMetadata: TransactionMetadata)
                                              (destination: string)
                                              (amount: TransferAmount)
                                              (privateKey: EthECKey) =

        let chain = GetNetwork account.Currency

        let trans =
            if account.Currency.IsEthToken() then
                SignEtherTokenTransaction chain txMetadata account.PublicAddress destination amount privateKey
            elif account.Currency.IsEtherBased() then
                if (txMetadata.Fee.Currency <> account.Currency) then
                    failwithf "Assertion failed: fee currency (%A) doesn't match with passed chain (%A)"
                              txMetadata.Fee.Currency account.Currency
                SignEtherTransaction chain txMetadata destination amount privateKey
            else
                failwithf "Assertion failed: Ether currency %A not supported?" account.Currency

        if not (signer.VerifyTransaction(trans, chain)) then
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
        let amount = TransferAmount(balance, balance, accountFrom.Currency)
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

    let private CreateInternal (password: string) (seed: array<byte>) =
        let privateKey = EthECKey(seed, true)
        let privateKeyBytes = privateKey.GetPrivateKeyAsBytes()
        let publicAddress = privateKey.GetPublicAddress()
        if not (addressUtil.IsChecksumAddress(publicAddress)) then
            failwith ("Nethereum's GetPublicAddress gave a non-checksum address: " + publicAddress)

        let accountSerializedJson =
            KeyStoreService.EncryptAndGenerateDefaultKeyStoreAsJson(password,
                                                                    privateKeyBytes,
                                                                    publicAddress)
        let fileNameForAccount = KeyStoreService.GenerateUTCFileName(publicAddress)
        fileNameForAccount,accountSerializedJson

    let Create (password: string) (seed: array<byte>) =
        async {
            return CreateInternal password seed
        }

    let public ExportUnsignedTransactionToJson trans =
        Marshalling.Serialize trans

    let SaveUnsignedTransaction (transProposal: UnsignedTransactionProposal)
                                (txMetadata: TransactionMetadata)
                                (readOnlyAccounts: seq<ReadOnlyAccount>)
                                    : string =

        let unsignedTransaction =
            {
                Proposal = transProposal;
                Cache = Caching.Instance.GetLastCachedData().ToDietCache readOnlyAccounts;
                Metadata = txMetadata;
            }
        ExportUnsignedTransactionToJson unsignedTransaction

