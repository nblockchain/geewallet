namespace GWallet.Backend.UtxoCoin

// NOTE: we can rename this file to less redundant "Account.fs" when this F# compiler bug is fixed:
// https://github.com/Microsoft/visualfsharp/issues/3231

open System
open System.Security
open System.Threading
open System.Linq

open NBitcoin

open GWallet.Backend

type internal TransactionOutpoint =
    {
        Transaction: Transaction;
        OutputIndex: int;
    }
    member self.ToCoin (): Coin =
        Coin(self.Transaction, uint32 self.OutputIndex)

type IUtxoAccount =
    inherit IAccount

    abstract member PublicKey: PubKey with get


type NormalUtxoAccount(currency: Currency, accountFile: FileRepresentation,
                       fromAccountFileToPublicAddress: FileRepresentation -> string,
                       fromAccountFileToPublicKey: FileRepresentation -> PubKey) =
    inherit GWallet.Backend.NormalAccount(currency, accountFile, fromAccountFileToPublicAddress)

    interface IUtxoAccount with
        member val PublicKey = fromAccountFileToPublicKey accountFile with get

type ReadOnlyUtxoAccount(currency: Currency, accountFile: FileRepresentation,
                         fromAccountFileToPublicAddress: FileRepresentation -> string,
                         fromAccountFileToPublicKey: FileRepresentation -> PubKey) =
    inherit GWallet.Backend.ReadOnlyAccount(currency, accountFile, fromAccountFileToPublicAddress)

    interface IUtxoAccount with
        member val PublicKey = fromAccountFileToPublicKey accountFile with get

type ArchivedUtxoAccount(currency: Currency, accountFile: FileRepresentation,
                         fromAccountFileToPublicAddress: FileRepresentation -> string,
                         fromAccountFileToPublicKey: FileRepresentation -> PubKey) =
    inherit GWallet.Backend.ArchivedAccount(currency, accountFile, fromAccountFileToPublicAddress)

    interface IUtxoAccount with
        member val PublicKey = fromAccountFileToPublicKey accountFile with get

module Account =

    type ElectrumServerDiscarded(message:string, innerException: Exception) =
       inherit Exception (message, innerException)

    let private MaxNumberOfParallelJobsForMode mode =
        match mode with
        | Mode.Fast -> 8u
        | Mode.Analysis -> 5u

    let private FaultTolerantParallelClientDefaultSettings(mode: Mode) =
        {
            NumberOfMaximumParallelJobs = MaxNumberOfParallelJobsForMode mode
            ConsistencyConfig = NumberOfConsistentResponsesRequired 2u;
            NumberOfRetries = Config.NUMBER_OF_RETRIES_TO_SAME_SERVERS;
            NumberOfRetriesForInconsistency = Config.NUMBER_OF_RETRIES_TO_SAME_SERVERS;
            Mode = mode
        }

    let private FaultTolerantParallelClientSettingsForBroadcast() =
        {
            FaultTolerantParallelClientDefaultSettings Mode.Fast with
                ConsistencyConfig = NumberOfConsistentResponsesRequired 1u;
        }

    let private faultTolerantElectrumClient =
        FaultTolerantParallelClient<string,ElectrumServerDiscarded> Caching.Instance.SaveServerLastStat

    let internal GetNetwork (currency: Currency) =
        if not (currency.IsUtxo()) then
            failwithf "Assertion failed: currency %A should be UTXO-type" currency
        match currency with
        | BTC -> Config.BitcoinNet
        | LTC -> Config.LitecoinNet
        | _ -> failwithf "Assertion failed: UTXO currency %A not supported?" currency

    // technique taken from https://electrumx.readthedocs.io/en/latest/protocol-basics.html#script-hashes
    let private GetElectrumScriptHashFromAddress (address: BitcoinAddress): string =
        let sha = NBitcoin.Crypto.Hashes.SHA256(address.ScriptPubKey.ToBytes())
        let reversedSha = sha.Reverse().ToArray()
        NBitcoin.DataEncoders.Encoders.Hex.EncodeData reversedSha

    let public GetElectrumScriptHashFromPublicAddress currency (publicAddress: string) =
        // TODO: measure how long does it take to get the script hash and if it's too long, cache it at app startup?
        BitcoinAddress.Create(publicAddress, GetNetwork currency) |> GetElectrumScriptHashFromAddress

    let internal GetPublicAddressFromPublicKey currency (publicKey: PubKey) =
        (publicKey.GetSegwitAddress (GetNetwork currency)).GetScriptAddress().ToString()

    let GetPublicAddressFromNormalAccountFile (currency: Currency) (accountFile: FileRepresentation): string =
        let pubKey = PubKey(accountFile.Name)
        GetPublicAddressFromPublicKey currency pubKey

    let GetPublicKeyFromNormalAccountFile (accountFile: FileRepresentation): PubKey =
        PubKey accountFile.Name

    let GetPublicKeyFromReadOnlyAccountFile (accountFile: FileRepresentation): PubKey =
        accountFile.Content() |> PubKey

    let GetPublicAddressFromUnencryptedPrivateKey (currency: Currency) (privateKey: string) =
        let privateKey = Key.Parse(privateKey, GetNetwork currency)
        GetPublicAddressFromPublicKey currency privateKey.PubKey

    // FIXME: there should be a way to simplify this function to not need to pass a new ad-hoc delegate
    //        (maybe make it more similar to old EtherServer.fs' PlumbingCall() in stable branch[1]?)
    //        [1] https://gitlab.com/knocte/gwallet/blob/stable/src/GWallet.Backend/EtherServer.fs
    let private GetRandomizedFuncs<'R> (currency: Currency)
                                          (electrumClientFunc: ElectrumServer->Async<'R>)
                                              : List<Server<string,'R>> =

        let ElectrumServerToRetrievalFunc (electrumServer: ElectrumServer)
                                          (electrumClientFunc: ElectrumServer->Async<'R>)
                                              : Async<'R> = async {
            try
                return! electrumClientFunc electrumServer
            with
            | ex ->
                if (ex :? ConnectionUnsuccessfulException ||
                    ex :? ElectrumServerReturningInternalErrorException ||
                    ex :? IncompatibleServerException) then
                    let msg = sprintf "%s: %s" (ex.GetType().FullName) ex.Message
                    raise (ElectrumServerDiscarded(msg, ex))
                match ex with
                | :? ElectrumServerReturningErrorException as esEx ->
                    return failwith (sprintf "Error received from Electrum server %s: '%s' (code '%d'). Original request: '%s'. Original response: '%s'."
                                      electrumServer.Fqdn
                                      esEx.Message
                                      esEx.ErrorCode
                                      esEx.OriginalRequest
                                      esEx.OriginalResponse)
                | _ ->
                    return raise <| FSharpUtil.ReRaise ex
        }

        let ElectrumServerToGenericServer (electrumClientFunc: ElectrumServer->Async<'R>)
                                          (electrumServer: ElectrumServer)
                                              : Server<string,'R> =
            { Identifier = electrumServer.Fqdn
              HistoryInfo = Caching.Instance.RetreiveLastServerHistory electrumServer.Fqdn
              Retrieval = ElectrumServerToRetrievalFunc electrumServer electrumClientFunc }

        let randomizedElectrumServers = ElectrumServerSeedList.Randomize currency |> List.ofSeq
        let randomizedServers =
            List.map (ElectrumServerToGenericServer electrumClientFunc)
                     randomizedElectrumServers
        randomizedServers

    let private GetBalances (account: IUtxoAccount) (mode: Mode) (cancelSourceOption: Option<CancellationTokenSource>)
                                : Async<BlockchainScripthahsGetBalanceInnerResult> =
        let scriptHashHex = GetElectrumScriptHashFromPublicAddress account.Currency account.PublicAddress

        let query =
            match cancelSourceOption with
            | None ->
                faultTolerantElectrumClient.Query
            | Some cancelSource ->
                faultTolerantElectrumClient.QueryWithCancellation cancelSource
        let balanceJob =
            query
                (FaultTolerantParallelClientDefaultSettings mode)
                (GetRandomizedFuncs account.Currency (ElectrumClient.GetBalance scriptHashHex))
        balanceJob

    let private GetBalancesFromServer (account: IUtxoAccount)
                                      (mode: Mode)
                                      (cancelSourceOption: Option<CancellationTokenSource>)
                                         : Async<Option<BlockchainScripthahsGetBalanceInnerResult>> =
        async {
            try
                let! balances = GetBalances account mode cancelSourceOption
                return Some balances
            with
            | ex ->
                if (FSharpUtil.FindException<ResourceUnavailabilityException> ex).IsSome then
                    return None
                else
                    return raise (FSharpUtil.ReRaise ex)
        }

    let internal GetShowableBalance (account: IUtxoAccount)
                                    (mode: Mode)
                                    (cancelSourceOption: Option<CancellationTokenSource>)
                                        : Async<Option<decimal>> =
        async {
            let! maybeBalances = GetBalancesFromServer account mode cancelSourceOption
            match maybeBalances with
            | Some balances ->
                let unconfirmedPlusConfirmed = balances.Unconfirmed + balances.Confirmed
                let amountToShowInSatoshis =
                    if unconfirmedPlusConfirmed < balances.Confirmed then
                        unconfirmedPlusConfirmed
                    else
                        balances.Confirmed
                let amountInBtc = (Money.Satoshis amountToShowInSatoshis).ToUnit MoneyUnit.BTC
                return Some amountInBtc
            | None ->
                return None
        }

    let private CreateTransactionAndCoinsToBeSigned (account: IUtxoAccount)
                                                    (transactionInputs: List<TransactionInputOutpointInfo>)
                                                    (destination: string)
                                                    (amount: TransferAmount)
                                                        : TransactionBuilder =
        let coins =
            seq {
                for input in transactionInputs do
                    let txHash = uint256(input.TransactionHash)

                    let scriptPubKeyInBytes = NBitcoin.DataEncoders.Encoders.Hex.DecodeData input.DestinationInHex
                    let scriptPubKey = Script(scriptPubKeyInBytes)

                    let coin = Coin(txHash,

                                    uint32 input.OutputIndex,

                                    Money(input.ValueInSatoshis),
                                    scriptPubKey)

                    let scriptCoin = coin.ToScriptCoin(account.PublicKey.WitHash.ScriptPubKey)
                    yield scriptCoin :> ICoin
            } |> List.ofSeq

        let transactionBuilder = (GetNetwork account.Currency).CreateTransactionBuilder()
        transactionBuilder.AddCoins coins |> ignore

        let currency = account.Currency
        let destAddress = BitcoinAddress.Create(destination, GetNetwork currency)

        if amount.BalanceAtTheMomentOfSending <> amount.ValueToSend then
            let moneyAmount = Money(amount.ValueToSend, MoneyUnit.BTC)
            transactionBuilder.Send(destAddress, moneyAmount) |> ignore
            let originAddress = (account :> IAccount).PublicAddress
            let changeAddress = BitcoinAddress.Create(originAddress, GetNetwork currency)
            transactionBuilder.SetChange changeAddress |> ignore
        else
            transactionBuilder.SendAll destAddress |> ignore

        // to enable RBF, see https://bitcoin.stackexchange.com/a/61038/2751
        transactionBuilder.SetLockTime (LockTime 0) |> ignore

        transactionBuilder

    type internal UnspentTransactionOutputInfo =
        {
            TransactionId: string;
            OutputIndex: int;
            Value: Int64;
        }

    let EstimateFee (account: IUtxoAccount) (amount: TransferAmount) (destination: string)
                        : Async<TransactionMetadata> = async {
        let rec addInputsUntilAmount (utxos: List<UnspentTransactionOutputInfo>)
                                      soFarInSatoshis
                                      amount
                                     (acc: List<UnspentTransactionOutputInfo>)
                                     : List<UnspentTransactionOutputInfo>*int64 =
            match utxos with
            | [] ->
                // should `raise InsufficientFunds` instead?
                failwith (sprintf "Not enough funds (needed: %s, got so far: %s)"
                                  (amount.ToString()) (soFarInSatoshis.ToString()))
            | utxoInfo::tail ->
                let newAcc = utxoInfo::acc

                let newSoFar = soFarInSatoshis + utxoInfo.Value
                if (newSoFar < amount) then
                    addInputsUntilAmount tail newSoFar amount newAcc
                else
                    newAcc,newSoFar

        let job = GetElectrumScriptHashFromPublicAddress account.Currency account.PublicAddress
                  |> ElectrumClient.GetUnspentTransactionOutputs
        let! utxos =
            faultTolerantElectrumClient.Query
                (FaultTolerantParallelClientDefaultSettings Mode.Fast)
                (GetRandomizedFuncs account.Currency job)

        if not (utxos.Any()) then
            failwith "No UTXOs found!"
        let possibleInputs =
            seq {
                for utxo in utxos do
                    yield { TransactionId = utxo.TxHash; OutputIndex = utxo.TxPos; Value = utxo.Value }
            }

        // first ones are the smallest ones
        let inputsOrderedByAmount = possibleInputs.OrderBy(fun utxo -> utxo.Value) |> List.ofSeq

        let amountInSatoshis = Money(amount.ValueToSend, MoneyUnit.BTC).Satoshi
        let utxosToUse,totalValueOfInputs =
            addInputsUntilAmount inputsOrderedByAmount 0L amountInSatoshis List.Empty

        let asyncInputs =
            seq {
                for utxo in utxosToUse do
                    yield async {
                        let job = ElectrumClient.GetBlockchainTransaction utxo.TransactionId
                        let! transRaw =
                            faultTolerantElectrumClient.Query
                                (FaultTolerantParallelClientDefaultSettings Mode.Fast)
                                (GetRandomizedFuncs account.Currency job)
                        let transaction = Transaction.Parse(transRaw, GetNetwork amount.Currency)
                        let txOut = transaction.Outputs.[utxo.OutputIndex]
                        // should suggest a ToHex() method to NBitcoin's TxOut type?
                        let valueInSatoshis = txOut.Value
                        let destination = txOut.ScriptPubKey.ToHex()
                        let ret = {
                            TransactionHash = transaction.GetHash().ToString();
                            OutputIndex = utxo.OutputIndex;
                            ValueInSatoshis = txOut.Value.Satoshi;
                            DestinationInHex = destination;
                        }
                        return ret
                    }
            }
        let! inputs = Async.Parallel asyncInputs

        let transactionDraftInputs = inputs |> List.ofArray

        let averageFee (feesFromDifferentServers: List<decimal>): decimal =
            let avg = feesFromDifferentServers.Sum() / decimal feesFromDifferentServers.Length
            avg

        let minResponsesRequired = 3u

        //querying for 1 will always return -1 surprisingly...
        let estimateFeeJob = ElectrumClient.EstimateFee 2

        let! btcPerKiloByteForFastTrans =
            faultTolerantElectrumClient.Query
                { FaultTolerantParallelClientDefaultSettings Mode.Fast with
                      ConsistencyConfig = AverageBetweenResponses (minResponsesRequired, averageFee) }
                (GetRandomizedFuncs account.Currency estimateFeeJob)

        let feeRate =
            try
                Money(btcPerKiloByteForFastTrans, MoneyUnit.BTC) |> FeeRate
            with
            | ex ->
                // we need more info in case this bug shows again: https://gitlab.com/DiginexGlobal/geewallet/issues/43
                raise <| Exception(sprintf "Could not create fee rate from %s btc per KB"
                                           (btcPerKiloByteForFastTrans.ToString()), ex)

        let transactionBuilder = CreateTransactionAndCoinsToBeSigned account
                                                                     transactionDraftInputs
                                                                     destination
                                                                     amount
        let estimatedMinerFee = transactionBuilder.EstimateFees feeRate

        let estimatedMinerFeeInSatoshis = estimatedMinerFee.Satoshi
        let minerFee = MinerFee(estimatedMinerFeeInSatoshis, DateTime.Now, account.Currency)

        return { Inputs = transactionDraftInputs; Fee = minerFee }
    }

    let private SignTransactionWithPrivateKey (account: IUtxoAccount)
                                              (txMetadata: TransactionMetadata)
                                              (destination: string)
                                              (amount: TransferAmount)
                                              (privateKey: Key) =

        let btcMinerFee = txMetadata.Fee
        let amountInSatoshis = Money(amount.ValueToSend, MoneyUnit.BTC).Satoshi

        let finalTransactionBuilder = CreateTransactionAndCoinsToBeSigned account txMetadata.Inputs destination amount

        finalTransactionBuilder.AddKeys privateKey |> ignore
        finalTransactionBuilder.SendFees (Money.Satoshis(btcMinerFee.EstimatedFeeInSatoshis)) |> ignore

        let finalTransaction = finalTransactionBuilder.BuildTransaction true
        let transCheckResultAfterSigning = finalTransaction.Check()
        if (transCheckResultAfterSigning <> TransactionCheckResult.Success) then
            failwith (sprintf "Transaction check failed after signing with %A" transCheckResultAfterSigning)

        if not (finalTransactionBuilder.Verify finalTransaction) then
            failwith "Something went wrong when verifying transaction"
        finalTransaction

    let internal GetPrivateKey (account: NormalAccount) password =
        let encryptedPrivateKey = account.GetEncryptedPrivateKey()
        let encryptedSecret = BitcoinEncryptedSecretNoEC(encryptedPrivateKey, GetNetwork (account:>IAccount).Currency)
        try
            encryptedSecret.GetKey(password)
        with
        | :? SecurityException ->
            raise (InvalidPassword)

    let SignTransaction (account: NormalUtxoAccount)
                        (txMetadata: TransactionMetadata)
                        (destination: string)
                        (amount: TransferAmount)
                        (password: string) =

        let privateKey = GetPrivateKey account password

        let signedTransaction = SignTransactionWithPrivateKey
                                    account
                                    txMetadata
                                    destination
                                    amount
                                    privateKey
        let rawTransaction = signedTransaction.ToHex()
        rawTransaction

    let private BroadcastRawTransaction currency (rawTx: string): Async<string> =
        let job = ElectrumClient.BroadcastTransaction rawTx
        let newTxIdJob =
            faultTolerantElectrumClient.Query
                (FaultTolerantParallelClientSettingsForBroadcast ())
                (GetRandomizedFuncs currency job)
        newTxIdJob

    let BroadcastTransaction currency (transaction: SignedTransaction<_>) =
        // FIXME: stop embedding TransactionInfo element in SignedTransaction<BTC>
        // and show the info from the RawTx, using NBitcoin to extract it
        BroadcastRawTransaction currency transaction.RawTransaction

    let SendPayment (account: NormalUtxoAccount)
                    (txMetadata: TransactionMetadata)
                    (destination: string)
                    (amount: TransferAmount)
                    (password: string)
                    =
        let baseAccount = account :> IAccount
        if (baseAccount.PublicAddress.Equals(destination, StringComparison.InvariantCultureIgnoreCase)) then
            raise DestinationEqualToOrigin

        let finalTransaction = SignTransaction account txMetadata destination amount password
        BroadcastRawTransaction baseAccount.Currency finalTransaction

    // TODO: maybe move this func to Backend.Account module, or simply inline it (simple enough)
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

    let SweepArchivedFunds (account: ArchivedUtxoAccount)
                           (balance: decimal)
                           (destination: IAccount)
                           (txMetadata: TransactionMetadata) =
        let currency = (account:>IAccount).Currency
        let network = GetNetwork currency
        let amount = TransferAmount(balance, balance, currency)
        let privateKey = Key.Parse(account.GetUnencryptedPrivateKey(), network)
        let signedTrans = SignTransactionWithPrivateKey
                              account txMetadata destination.PublicAddress amount privateKey
        BroadcastRawTransaction currency (signedTrans.ToHex())

    let Create currency (password: string) (seed: array<byte>): Async<FileRepresentation> =
        async {
            let privKey = Key seed
            let network = GetNetwork currency
            let secret = privKey.GetBitcoinSecret network
            let encryptedSecret = secret.PrivateKey.GetEncryptedBitcoinSecret(password, network)
            let encryptedPrivateKey = encryptedSecret.ToWif()
            let publicKey = secret.PubKey.ToString()
            return {
                Name = publicKey
                Content = fun _ -> encryptedPrivateKey
            }
        }

    let ValidateAddress (currency: Currency) (address: string) =
        if String.IsNullOrEmpty address then
            raise <| ArgumentNullException "address"

        let BITCOIN_ADDRESS_BECH32_PREFIX = "bc1"

        let utxoCoinValidAddressPrefixes =
            match currency with
            | BTC ->
                let BITCOIN_ADDRESS_PUBKEYHASH_PREFIX = "1"
                let BITCOIN_ADDRESS_SCRIPTHASH_PREFIX = "3"
                [
                    BITCOIN_ADDRESS_PUBKEYHASH_PREFIX
                    BITCOIN_ADDRESS_SCRIPTHASH_PREFIX
                    BITCOIN_ADDRESS_BECH32_PREFIX
                ]
            | LTC ->
                let LITECOIN_ADDRESS_PUBKEYHASH_PREFIX = "L"
                let LITECOIN_ADDRESS_SCRIPTHASH_PREFIX = "M"
                [ LITECOIN_ADDRESS_PUBKEYHASH_PREFIX; LITECOIN_ADDRESS_SCRIPTHASH_PREFIX ]
            | _ -> failwithf "Unknown UTXO currency %A" currency

        if not (utxoCoinValidAddressPrefixes.Any(fun prefix -> address.StartsWith prefix)) then
            raise (AddressMissingProperPrefix(utxoCoinValidAddressPrefixes))

        let minLength,lenghtInBetweenAllowed,maxLength =
            if currency = Currency.BTC && (address.StartsWith BITCOIN_ADDRESS_BECH32_PREFIX) then
                // taken from https://github.com/bitcoin/bips/blob/master/bip-0173.mediawiki
                // (FIXME: this is only valid for the first version of segwit, fix it!)
                42,false,62
            else
                27,true,34
        let limits = [ minLength; maxLength ]
        if address.Length > maxLength then
            raise <| AddressWithInvalidLength limits
        if address.Length < minLength then
            raise <| AddressWithInvalidLength limits
        if not lenghtInBetweenAllowed && (address.Length <> minLength && address.Length <> maxLength) then
            raise <| AddressWithInvalidLength limits

        let network = GetNetwork currency
        try
            BitcoinAddress.Create(address, network) |> ignore
        with
        // TODO: propose to NBitcoin upstream to generate an NBitcoin exception instead
        | :? FormatException ->
            raise (AddressWithInvalidChecksum None)
