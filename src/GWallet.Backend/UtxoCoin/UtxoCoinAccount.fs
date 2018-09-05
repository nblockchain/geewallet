namespace GWallet.Backend.UtxoCoin

// NOTE: we can rename this file to less redundant "Account.fs" when this F# compiler bug is fixed:
// https://github.com/Microsoft/visualfsharp/issues/3231

open System
open System.IO
open System.Security
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

module internal Account =

    type ElectrumServerDiscarded(message:string, innerException: Exception) =
       inherit Exception (message, innerException)

    let private FaultTolerantParallelClientSettings() =
        {
            NumberOfMaximumParallelJobs = uint16 5;
            ConsistencyConfig = NumberOfConsistentResponsesRequired (uint16 2);
            NumberOfRetries = Config.NUMBER_OF_RETRIES_TO_SAME_SERVERS;
            NumberOfRetriesForInconsistency = Config.NUMBER_OF_RETRIES_TO_SAME_SERVERS;
        }

    let private faultTolerantElectrumClient =
        FaultTolerantParallelClient<ElectrumServerDiscarded>()

    let internal GetNetwork (currency: Currency) =
        if not (currency.IsUtxo()) then
            failwithf "Assertion failed: currency %A should be UTXO-type" currency
        match currency with
        | BTC -> Config.BitcoinNet
        | LTC -> Config.LitecoinNet
        | _ -> failwithf "Assertion failed: UTXO currency %A not supported?" currency

    let private GetPublicAddressFromPublicKey currency (publicKey: PubKey) =
        (publicKey.GetSegwitAddress (GetNetwork currency)).GetScriptAddress().ToString()

    let GetPublicAddressFromAccountFile currency (accountFile: FileInfo) =
        let pubKey = new PubKey(accountFile.Name)
        GetPublicAddressFromPublicKey currency pubKey

    let GetPublicAddressFromUnencryptedPrivateKey (currency: Currency) (privateKey: string) =
        let privateKey = Key.Parse(privateKey, GetNetwork currency)
        GetPublicAddressFromPublicKey currency privateKey.PubKey

    // FIXME: there should be a way to simplify this function to not need to pass a new ad-hoc delegate
    //        (maybe make it more similar to old EtherServer.fs' PlumbingCall() in stable branch[1]?)
    //        [1] https://gitlab.com/knocte/gwallet/blob/stable/src/GWallet.Backend/EtherServer.fs
    let private GetRandomizedFuncs<'T,'R> currency (ecFunc: ElectrumClient->'T->'R): list<'T->'R> =
        let randomizedServers = ElectrumServerSeedList.Randomize currency |> List.ofSeq
        let randomizedFuncs =
            List.map (fun (es:ElectrumServer) ->
                          (fun (arg: 'T) ->
                              try
                                  let electrumClient = ElectrumClient es
                                  ecFunc electrumClient arg
                              with
                              | ex ->
                                  if (ex :? JsonRpcSharp.ConnectionUnsuccessfulException ||
                                      ex :? ElectrumServerReturningInternalErrorException ||
                                      ex :? IncompatibleServerException) then
                                      let msg = sprintf "%s: %s" (ex.GetType().FullName) ex.Message
                                      raise (ElectrumServerDiscarded(msg, ex))
                                  match ex with
                                  | :? ElectrumServerReturningErrorException as esEx ->
                                      failwith (sprintf "Error received from Electrum server %s: '%s' (code '%d'). Original request: '%s'. Original response: '%s'."
                                                        es.Fqdn esEx.Message esEx.ErrorCode esEx.OriginalRequest esEx.OriginalResponse)
                                  | _ ->
                                      reraise()
                           )
                     )
                     randomizedServers
        randomizedFuncs

    let private GetBalance(account: IAccount) =
        let electrumGetBalance (ec: ElectrumClient) (address: string) =
            ec.GetBalance address
        let balance =
            faultTolerantElectrumClient.Query<string,BlockchainAddressGetBalanceInnerResult>
                (FaultTolerantParallelClientSettings())
                account.PublicAddress
                (GetRandomizedFuncs account.Currency electrumGetBalance)
        balance

    let GetConfirmedBalance(account: IAccount): Async<decimal> =
        async {
            let! balance = GetBalance account
            let confirmedBalance = balance.Confirmed |> UnitConversion.FromSatoshiToBtc
            return confirmedBalance
        }

    let GetUnconfirmedPlusConfirmedBalance(account: IAccount): Async<decimal> =
        async {
            let! balance = GetBalance account
            let confirmedBalance = balance.Unconfirmed + balance.Confirmed |> UnitConversion.FromSatoshiToBtc
            return confirmedBalance
        }

    let private CreateTransactionAndCoinsToBeSigned currency (transactionDraft: TransactionDraft): Transaction*list<Coin> =
        let transaction = Transaction()
        let coins =
            seq {
                for input in transactionDraft.Inputs do
                    let nbitcoinInput = TxIn()
                    let txHash = uint256(input.TransactionHash)
                    nbitcoinInput.PrevOut.Hash <- txHash
                    nbitcoinInput.PrevOut.N <- uint32 input.OutputIndex
                    let inputAdded = transaction.AddInput nbitcoinInput

                    // mark RBF=enabled by default
                    inputAdded.Sequence <- Sequence(0)
                    if not inputAdded.Sequence.IsRBF then
                        failwith "input should have been marked as RBF by default"

                    let scriptPubKeyInBytes = NBitcoin.DataEncoders.Encoders.Hex.DecodeData input.DestinationInHex
                    let scriptPubKey = Script(scriptPubKeyInBytes)

                    yield Coin(txHash,
                               nbitcoinInput.PrevOut.N,
                               Money(input.ValueInSatoshis),
                               scriptPubKey)
            } |> List.ofSeq

        for output in transactionDraft.Outputs do
            let destAddress = BitcoinAddress.Create(output.DestinationAddress, GetNetwork currency)
            let txOut = TxOut(Money(output.ValueInSatoshis), destAddress)
            transaction.Outputs.Add(txOut)

        if not transaction.RBF then
            failwith "transaction should have been marked as RBF by default"
        if transaction.LockTime.IsTimeLock then
            failwith "transaction shouldn't be marked as time lock"
        if transaction.LockTime.Height <> 0 then
            failwith "transaction height shouldn't be different than 0"
        transaction,coins

    type internal UnspentTransactionOutputInfo =
        {
            TransactionId: string;
            OutputIndex: int;
            Value: Int64;
        }

    let private SubstractMinerFeeToTransactionDraft (transactionDraftWithoutMinerFee: TransactionDraft)
                                                    (amountToBeSentInSatoshisNotConsideringChange)
                                                    (minerFeeInSatoshis)
            : TransactionDraft =

        let newOutputs = seq {

            // maybe if we change this argument to be a discriminated union we would not need this <1 and >2 bullshit?
            if transactionDraftWithoutMinerFee.Outputs.Length < 1 then
                failwith "transactionDraftWithoutMinerFee should have output(s)"
            elif transactionDraftWithoutMinerFee.Outputs.Length > 2 then
                failwithf "transactionDraftWithoutMinerFee should have 1 or 2 outputs, not more (now %d)"
                          transactionDraftWithoutMinerFee.Outputs.Length
            else
                let firstOutput = transactionDraftWithoutMinerFee.Outputs.First()
                if (amountToBeSentInSatoshisNotConsideringChange <> firstOutput.ValueInSatoshis) then
                    failwith "Assertion failed: amount and transactionDraft's amount (first output) don't match"

                let lastOutput = transactionDraftWithoutMinerFee.Outputs.Last()
                let valueInSatoshisMinusMinerFee = lastOutput.ValueInSatoshis - minerFeeInSatoshis
                if not (valueInSatoshisMinusMinerFee > 0L) then
                    let minerFeeInMainCurrency = minerFeeInSatoshis |> UnitConversion.FromSatoshiToBtc
                    raise (InsufficientBalanceForFee minerFeeInMainCurrency)

                let newOutput =
                    { lastOutput with ValueInSatoshis = valueInSatoshisMinusMinerFee; }
                if transactionDraftWithoutMinerFee.Outputs.Length = 2 then
                    yield firstOutput
                yield newOutput
        }

        { transactionDraftWithoutMinerFee with Outputs = newOutputs |> List.ofSeq }

    // this is a rough guess between 3 tests with 1, 2 and 3 inputs:
    // 1input  -> 215(total): 83+(X*1)
    // 2inputs -> 386(total): 124+(X*2)
    // 3inputs -> 559(total): 165+(X*3)  ... therefore X = 131?
    // FIXME: anyway I should use NBitcoin's estimation facilicities
    //        (i.e. by using TransactionBuilder, however, not before this bug gets fixed upstream: https://github.com/MetacoSA/NBitcoin/issues/396 )
    let private BYTES_PER_INPUT_ESTIMATION_CONSTANT = 131

    let EstimateFee account (amount: TransferAmount) (destination: string): Async<TransactionMetadata> = async {
        let rec addInputsUntilAmount (utxos: list<UnspentTransactionOutputInfo>)
                                      soFarInSatoshis
                                      amount
                                     (acc: list<UnspentTransactionOutputInfo>)
                                     : list<UnspentTransactionOutputInfo>*int64 =
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

        let baseAccount = (account:>IAccount)

        let electrumGetUtxos (ec: ElectrumClient) (address: string) =
            ec.GetUnspentTransactionOutputs address
        let! utxos =
            faultTolerantElectrumClient.Query<string,array<BlockchainAddressListUnspentInnerResult>>
                (FaultTolerantParallelClientSettings())
                baseAccount.PublicAddress
                (GetRandomizedFuncs baseAccount.Currency electrumGetUtxos)

        if not (utxos.Any()) then
            failwith "No UTXOs found!"
        let possibleInputs =
            seq {
                for utxo in utxos do
                    yield { TransactionId = utxo.TxHash; OutputIndex = utxo.TxPos; Value = utxo.Value }
            }

        // first ones are the smallest ones
        let inputsOrderedByAmount = possibleInputs.OrderBy(fun utxo -> utxo.Value) |> List.ofSeq

        let amountInSatoshis = UnitConversion.FromBtcToSatoshis amount.ValueToSend
        let utxosToUse,totalValueOfInputs =
            addInputsUntilAmount inputsOrderedByAmount 0L amountInSatoshis []

        let electrumGetTx (ec: ElectrumClient) (txId: string) =
            ec.GetBlockchainTransaction txId

        let asyncInputs =
            seq {
                for utxo in utxosToUse do
                    yield async {
                        let! transRaw =
                            faultTolerantElectrumClient.Query<string,string>
                                (FaultTolerantParallelClientSettings())
                                utxo.TransactionId
                                (GetRandomizedFuncs baseAccount.Currency electrumGetTx)
                        let transaction = Transaction(transRaw)
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

        let outputs =
            seq {
                yield { ValueInSatoshis = amountInSatoshis; DestinationAddress = destination }
                if (amountInSatoshis <> totalValueOfInputs) then
                    if (amount.ValueToSend = amount.BalanceAtTheMomentOfSending) then
                        failwithf "Assertion failed: amountInSatoshis(%d)<>totalValueOfInputs(%d) but ValueToSend=BalanceAtTheMomentOfSending?"
                                  amountInSatoshis totalValueOfInputs
                    let changeAmount = totalValueOfInputs - amountInSatoshis
                    yield { ValueInSatoshis = changeAmount; DestinationAddress = baseAccount.PublicAddress }
            } |> List.ofSeq

        let transactionDraftWithoutMinerFee = { Inputs = inputs |> List.ofArray; Outputs = outputs }
        let unsignedTransaction,_ =
            CreateTransactionAndCoinsToBeSigned baseAccount.Currency transactionDraftWithoutMinerFee

        let electrumEstimateFee (ec: ElectrumClient) (targetNumBlocks: int) =
            ec.EstimateFee targetNumBlocks

        let transactionSizeInBytes = unsignedTransaction.ToBytes().Length
        //Console.WriteLine("transactionSize in bytes before signing: " + transactionSizeInBytes.ToString())
        let numberOfInputs = transactionDraftWithoutMinerFee.Inputs.Length
        let estimatedFinalTransSize =
            transactionSizeInBytes + (BYTES_PER_INPUT_ESTIMATION_CONSTANT * unsignedTransaction.Inputs.Count)
            + (numberOfInputs - 1)

        let averageFee (feesFromDifferentServers: List<decimal>): decimal =
            let avg = feesFromDifferentServers.Sum() / decimal feesFromDifferentServers.Length
            avg

        let minResponsesRequired = uint16 3
        let! btcPerKiloByteForFastTrans =
            faultTolerantElectrumClient.Query<int,decimal>
                { FaultTolerantParallelClientSettings() with
                      ConsistencyConfig = AverageBetweenResponses (minResponsesRequired, averageFee) }
                //querying for 1 will always return -1 surprisingly...
                2
                (GetRandomizedFuncs baseAccount.Currency electrumEstimateFee)

        let minerFee = MinerFee(estimatedFinalTransSize, btcPerKiloByteForFastTrans, DateTime.Now, account.Currency)
        let minerFeeInSatoshis = minerFee.CalculateAbsoluteValueInSatoshis()

        let transactionWithMinerFeeSubstracted =
            SubstractMinerFeeToTransactionDraft transactionDraftWithoutMinerFee amountInSatoshis minerFeeInSatoshis

        return { TransactionDraft = transactionWithMinerFeeSubstracted; Fee = minerFee }
    }

    let private SignTransactionWithPrivateKey (currency: Currency)
                                              (txMetadata: TransactionMetadata)
                                              (destination: string)
                                              (amount: TransferAmount)
                                              (privateKey: Key) =

        let transactionWithMinerFeeSubstracted = txMetadata.TransactionDraft
        let btcMinerFee = txMetadata.Fee
        let amountInSatoshis = UnitConversion.FromBtcToSatoshis amount.ValueToSend

        if (transactionWithMinerFeeSubstracted.Outputs.Length < 1 ||
            transactionWithMinerFeeSubstracted.Outputs.Length > 2) then
            failwith (sprintf "draftTransaction should have 1 or 2 outputs, not more, not less (now %d)"
                              transactionWithMinerFeeSubstracted.Outputs.Length)
        if (transactionWithMinerFeeSubstracted.Outputs.[0].DestinationAddress <> destination) then
            failwith "Destination address and the first output's destination address should match"

        // it means we're sending all balance!
        if (amount.ValueToSend = amount.BalanceAtTheMomentOfSending) then
            if (transactionWithMinerFeeSubstracted.Outputs.Length <> 1) then
                failwith (sprintf "Assertion outputsCount==1 failed (it was %d)"
                                  transactionWithMinerFeeSubstracted.Outputs.Length)
        // it means there's change involved (send change back to change-address)
        else
            if (transactionWithMinerFeeSubstracted.Outputs.Length <> 2) then
                failwith (
                    sprintf "Assertion failed: outputsCount should be 2 but it was %d (ValueToSend: %M; BalanceAtTheMomentOfSending: %M)"
                        transactionWithMinerFeeSubstracted.Outputs.Length
                        amount.ValueToSend
                        amount.BalanceAtTheMomentOfSending
                )


        let finalTransaction,coins = CreateTransactionAndCoinsToBeSigned currency transactionWithMinerFeeSubstracted

        let coinsToSign =
            coins.Select(fun c -> c :> ICoin)
            |> Seq.toArray

        let transCheckResultBeforeSigning = finalTransaction.Check()
        if (transCheckResultBeforeSigning <> TransactionCheckResult.Success) then
            failwith (sprintf "Transaction check failed before signing with %A" transCheckResultBeforeSigning)
        finalTransaction.Sign(privateKey, coinsToSign)
        let transCheckResultAfterSigning = finalTransaction.Check()
        if (transCheckResultAfterSigning <> TransactionCheckResult.Success) then
            failwith (sprintf "Transaction check failed after signing with %A" transCheckResultAfterSigning)

        let maxDeviationAllowedForEstimationToNotBeConsideredAnError = 2
        let transSizeAfterSigning = finalTransaction.ToBytes().Length
        //Console.WriteLine (sprintf "Transaction size after signing: %d bytes" transSizeAfterSigning)
        let differenceBetweenRealSizeAndEstimated = transSizeAfterSigning - btcMinerFee.EstimatedTransactionSizeInBytes
        if (Math.Abs(differenceBetweenRealSizeAndEstimated) > maxDeviationAllowedForEstimationToNotBeConsideredAnError) then
            failwith (sprintf "Transaction size estimation failed, got %d but calculated %d bytes (a difference of %d, with %d inputs)"
                              transSizeAfterSigning btcMinerFee.EstimatedTransactionSizeInBytes
                              (transSizeAfterSigning - btcMinerFee.EstimatedTransactionSizeInBytes)
                              finalTransaction.Inputs.Count)
        finalTransaction

    let internal GetPrivateKey (account: NormalAccount) password =
        let encryptedPrivateKey = File.ReadAllText(account.AccountFile.FullName)
        let encryptedSecret = BitcoinEncryptedSecretNoEC(encryptedPrivateKey, GetNetwork (account:>IAccount).Currency)
        try
            encryptedSecret.GetKey(password)
        with
        | :? SecurityException ->
            raise (InvalidPassword)

    let SignTransaction (account: NormalAccount)
                        (txMetadata: TransactionMetadata)
                        (destination: string)
                        (amount: TransferAmount)
                        (password: string) =

        let privateKey = GetPrivateKey account password

        let signedTransaction = SignTransactionWithPrivateKey
                                    (account:>IAccount).Currency
                                    txMetadata
                                    destination
                                    amount
                                    privateKey
        let rawTransaction = signedTransaction.ToHex()
        rawTransaction

    let private BroadcastRawTransaction currency (rawTx: string) =
        let electrumBroadcastTx (ec: ElectrumClient) (rawTx: string): string =
            ec.BroadcastTransaction rawTx
        let newTxId =
            faultTolerantElectrumClient.Query<string,string>
                (FaultTolerantParallelClientSettings())
                rawTx
                (GetRandomizedFuncs currency electrumBroadcastTx)
        newTxId

    let BroadcastTransaction currency (transaction: SignedTransaction<_>) =
        // FIXME: stop embedding TransactionInfo element in SignedTransaction<BTC>
        // and show the info from the RawTx, using NBitcoin to extract it
        BroadcastRawTransaction currency transaction.RawTransaction

    let SendPayment (account: NormalAccount)
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

    let SweepArchivedFunds (account: ArchivedAccount)
                           (balance: decimal)
                           (destination: IAccount)
                           (txMetadata: TransactionMetadata) =
        let currency = (account:>IAccount).Currency
        let network = GetNetwork currency
        let amount = TransferAmount(balance, balance, currency)
        let privateKey = Key.Parse(account.PrivateKey, network)
        let signedTrans = SignTransactionWithPrivateKey
                              currency txMetadata destination.PublicAddress amount privateKey
        BroadcastRawTransaction currency (signedTrans.ToHex())

    let private LENGTH_OF_PRIVATE_KEYS = 32
    let private CreateInternal currency (password: string) (seed: array<byte>) =
        let privkey = Key(seed)
        let network = GetNetwork currency
        let secret = privkey.GetBitcoinSecret network
        let encryptedSecret = secret.PrivateKey.GetEncryptedBitcoinSecret(password, network)
        let encryptedPrivateKey = encryptedSecret.ToWif()
        let publicKey = secret.PubKey.ToString()
        publicKey,encryptedPrivateKey

    let Create currency (password: string) (seed: array<byte>) =
        async {
            return CreateInternal currency password seed
        }

    let ValidateAddress (currency: Currency) (address: string) =
        let UTXOCOIN_MIN_ADDRESSES_LENGTH = 27
        let UTXOCOIN_MAX_ADDRESSES_LENGTH = 34

        let utxoCoinValidAddressPrefixes =
            match currency with
            | BTC ->
                let BITCOIN_ADDRESS_PUBKEYHASH_PREFIX = "1"
                let BITCOIN_ADDRESS_SCRIPTHASH_PREFIX = "3"
                [ BITCOIN_ADDRESS_PUBKEYHASH_PREFIX; BITCOIN_ADDRESS_SCRIPTHASH_PREFIX ]
            | LTC ->
                let LITECOIN_ADDRESS_PUBKEYHASH_PREFIX = "L"
                let LITECOIN_ADDRESS_SCRIPTHASH_PREFIX = "M"
                [ LITECOIN_ADDRESS_PUBKEYHASH_PREFIX; LITECOIN_ADDRESS_SCRIPTHASH_PREFIX ]
            | _ -> failwithf "Unknown UTXO currency %A" currency

        if not (utxoCoinValidAddressPrefixes.Any(fun prefix -> address.StartsWith prefix)) then
            raise (AddressMissingProperPrefix(utxoCoinValidAddressPrefixes))

        if (address.Length > UTXOCOIN_MAX_ADDRESSES_LENGTH) then
            raise (AddressWithInvalidLength(UTXOCOIN_MAX_ADDRESSES_LENGTH))
        if (address.Length < UTXOCOIN_MIN_ADDRESSES_LENGTH) then
            raise (AddressWithInvalidLength(UTXOCOIN_MIN_ADDRESSES_LENGTH))

        let network = GetNetwork currency
        try
            BitcoinAddress.Create(address, network) |> ignore
        with
        // TODO: propose to NBitcoin upstream to generate an NBitcoin exception instead
        | :? FormatException ->
            raise (AddressWithInvalidChecksum None)
