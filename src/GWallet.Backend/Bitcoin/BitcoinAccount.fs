namespace GWallet.Backend.Bitcoin

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

    let GetPublicAddressFromAccountFile (accountFile: FileInfo) =
        let pubKey = new PubKey(accountFile.Name)
        pubKey.GetSegwitAddress(Network.Main).GetScriptAddress().ToString()


    // TODO: return MaybeCached<decimal>
    let GetBalance(account: IAccount): decimal =
        let electrumServer = ElectrumServer.PickRandom()
        use electrumClient = new ElectrumClient(electrumServer)
        electrumClient.GetBalance account.PublicAddress |> UnitConversion.FromSatoshiToBTC

    let private CreateTransactionAndCoinsToBeSigned (transactionDraft: TransactionDraft): Transaction*list<Coin> =
        let transaction = Transaction()
        let coins =
            seq {
                for input in transactionDraft.Inputs do
                    let inputTx = Transaction(input.RawTransaction)
                    transaction.AddInput(inputTx, input.OutputIndex) |> ignore
                    yield Coin(inputTx, uint32 input.OutputIndex)
            } |> List.ofSeq

        for output in transactionDraft.Outputs do
            let destAddress = BitcoinAddress.Create(output.DestinationAddress, Network.Main)
            let txOut = TxOut(Money(output.ValueInSatoshis), destAddress)
            transaction.Outputs.Add(txOut)

        transaction,coins

    type internal UnspentTransactionOutputInfo =
        {
            TransactionId: string;
            OutputIndex: int;
            Value: Int64;
        }

    // this is a rough guess between 3 tests with 1, 2 and 3 inputs:
    // 1input  -> 215(total): 83+(X*1)
    // 2inputs -> 386(total): 124+(X*2)
    // 3inputs -> 559(total): 165+(X*3)  ... therefore X = 131?
    // FIXME: anyway I should use NBitcoin's estimation facilicities
    let private BYTES_PER_INPUT_ESTIMATION_CONSTANT = 131

    let EstimateFee account (amount: decimal) (destination: string) =
        let rec addInputsUntilAmount (utxos: list<UnspentTransactionOutputInfo>)
                                      soFarInSatoshis
                                      amount
                                     (acc: list<UnspentTransactionOutputInfo>)
                                     : list<UnspentTransactionOutputInfo>*int64 =
            match utxos with
            | [] ->
                failwith (sprintf "Not enough funds (needed: %s, got so far: %s)"
                                  (amount.ToString()) (soFarInSatoshis.ToString()))
            | utxoInfo::tail ->
                let newAcc = utxoInfo::acc

                let newSoFar = soFarInSatoshis + utxoInfo.Value
                if (newSoFar < amount) then
                    addInputsUntilAmount tail newSoFar amount newAcc
                else
                    newAcc,newSoFar

        let electrumServer = ElectrumServer.PickRandom()
        use electrumClient = new ElectrumClient(electrumServer)
        let utxos = electrumClient.GetUnspentTransactionOutputs (account:>IAccount).PublicAddress
        if not (utxos.Any()) then
            failwith "No UTXOs found!"
        let possibleInputs =
            seq {
                for utxo in utxos do
                    yield { TransactionId = utxo.TxHash; OutputIndex = utxo.TxPos; Value = utxo.Value }
            }

        // first ones are the smallest ones
        let inputsOrderedByAmount = possibleInputs.OrderBy(fun utxo -> utxo.Value) |> List.ofSeq

        let amountInSatoshis = Convert.ToInt64(amount * 100000000m)
        let utxosToUse,totalValueOfInputs =
            addInputsUntilAmount inputsOrderedByAmount 0L amountInSatoshis []

        let inputs =
            seq {
                for utxo in utxosToUse do
                    let transRaw = electrumClient.GetBlockchainTransaction utxo.TransactionId
                    yield { RawTransaction = transRaw; OutputIndex = utxo.OutputIndex }
            } |> List.ofSeq

        let outputs =
            seq {
                yield { ValueInSatoshis = amountInSatoshis; DestinationAddress = destination }
                if (amountInSatoshis <> totalValueOfInputs) then
                    let changeAmount = totalValueOfInputs - amountInSatoshis
                    yield { ValueInSatoshis = changeAmount; DestinationAddress = (account:>IAccount).PublicAddress }
            } |> List.ofSeq

        let transactionDraftWithoutMinerFee = { Inputs = inputs; Outputs = outputs }
        let unsignedTransaction,_ = CreateTransactionAndCoinsToBeSigned transactionDraftWithoutMinerFee

        let transactionSizeInBytes = unsignedTransaction.ToBytes().Length
        //Console.WriteLine("transactionSize in bytes before signing: " + transactionSizeInBytes.ToString())
        let numberOfInputs = transactionDraftWithoutMinerFee.Inputs.Length
        let estimatedFinalTransSize =
            transactionSizeInBytes + (BYTES_PER_INPUT_ESTIMATION_CONSTANT * unsignedTransaction.Inputs.Count)
            + (numberOfInputs - 1)
        let btcPerKiloByteForFastTrans = electrumClient.EstimateFee 2 //querying for 1 will always return -1 surprisingly...

        MinerFee(estimatedFinalTransSize, btcPerKiloByteForFastTrans, DateTime.Now, transactionDraftWithoutMinerFee)

    let private SubstractMinerFeeToTransactionDraft (transactionDraftWithoutMinerFee: TransactionDraft)
                                                    (amountToBeSentInSatoshisNotConsideringChange)
                                                    (minerFeeInSatoshis)
            : TransactionDraft =

        let newOutputs =
            match transactionDraftWithoutMinerFee.Outputs.Length with
            | 0 ->
                failwith "transactionDraftWithoutMinerFee should have output(s)"
            | 1 ->
                let singleOutput = transactionDraftWithoutMinerFee.Outputs.First()
                if (amountToBeSentInSatoshisNotConsideringChange <> singleOutput.ValueInSatoshis) then
                    failwith "amount and transactionDraft's amount don't match"
                let valueInSatoshisMinusMinerFee = singleOutput.ValueInSatoshis - minerFeeInSatoshis
                let newSingleOutput =
                    { ValueInSatoshis = valueInSatoshisMinusMinerFee;
                      DestinationAddress = singleOutput.DestinationAddress; }
                [ newSingleOutput ]
            | 2 ->
                let mainOutput = transactionDraftWithoutMinerFee.Outputs.First()
                if (amountToBeSentInSatoshisNotConsideringChange <> mainOutput.ValueInSatoshis) then
                    failwith "amount and transactionDraft's amount of first output should be equal (by convention first output is not the change output!)"
                let changeOutput = transactionDraftWithoutMinerFee.Outputs.[1]
                let newChangeOutput =
                    { ValueInSatoshis = changeOutput.ValueInSatoshis - minerFeeInSatoshis;
                      DestinationAddress = changeOutput.DestinationAddress; }
                [ mainOutput; newChangeOutput ]

            | unexpectedCount ->
                failwith (sprintf "transactionDraftWithoutMinerFee should have 1 or 2 outputs, not more (now %d)"
                                  unexpectedCount)

        { Inputs = transactionDraftWithoutMinerFee.Inputs; Outputs = newOutputs }

    let SendPayment (account: NormalAccount) (destination: string) (amount: TransferAmount)
                    (password: string)
                    (btcMinerFee: MinerFee)
                    =
        let transactionDraft = btcMinerFee.DraftTransaction
        let minerFee = btcMinerFee :> IBlockchainFee
        let minerFeeInSatoshis = Convert.ToInt64(minerFee.Value * 100000000m)
        let amountInSatoshis = Convert.ToInt64(amount.ValueToSend * 100000000m)

        if (transactionDraft.Outputs.Length < 1 || transactionDraft.Outputs.Length > 2) then
            failwith (sprintf "draftTransaction should have 1 or 2 outputs, not more, not less (now %d)"
                              transactionDraft.Outputs.Length)
        if (transactionDraft.Outputs.[0].DestinationAddress <> destination) then
            failwith "Destination address and the first output's destination address should match"
        if (amount.IdealValueRemainingAfterSending < 0.0m) then
            failwith "Assertian idealValueRemainingAfterSending cannot be negative"

        // it means we're sending all balance!
        if (amount.IdealValueRemainingAfterSending = 0.0m && transactionDraft.Outputs.Length <> 1) then
            failwith (sprintf "Assertion outputsCount==1 failed (it was %d)" transactionDraft.Outputs.Length)
        // it means there's change involved (send change back to change-address)
        if (amount.IdealValueRemainingAfterSending > 0.0m && transactionDraft.Outputs.Length <> 2) then
            failwith (sprintf "Assertion outputsCount==2 failed (it was %d)" transactionDraft.Outputs.Length)

        let transactionWithMinerFeeSubstracted =
            SubstractMinerFeeToTransactionDraft transactionDraft amountInSatoshis minerFeeInSatoshis

        let finalTransaction,coins = CreateTransactionAndCoinsToBeSigned transactionWithMinerFeeSubstracted

        let encryptedPrivateKey = File.ReadAllText(account.AccountFile.FullName)
        let encryptedSecret = BitcoinEncryptedSecretNoEC(encryptedPrivateKey, Network.Main)

        let privateKey =
            try
                encryptedSecret.GetKey(password)
            with
            | :? SecurityException ->
                raise (InvalidPassword)

        // needed to sign with SegWit:
        let coinsToSign =
            coins.Select(fun c -> c.ToScriptCoin(privateKey.PubKey.WitHash.ScriptPubKey) :> ICoin)
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
        let electrumServer = ElectrumServer.PickRandom()
        use electrumClient = new ElectrumClient(electrumServer)
        let newTxId = electrumClient.BroadcastTransaction (finalTransaction.ToHex())
        newTxId

    let Create password =
        let privkey = Key()
        let secret = privkey.GetBitcoinSecret(Network.Main)
        let encryptedSecret = secret.PrivateKey.GetEncryptedBitcoinSecret(password, Network.Main)
        let encryptedPrivateKey = encryptedSecret.ToWif()
        let publicKey = secret.PubKey.ToString()
        publicKey,encryptedPrivateKey

    let ValidateAddress (address: string) =
        let BITCOIN_MIN_ADDRESSES_LENGTH = 27
        let BITCOIN_MAX_ADDRESSES_LENGTH = 34

        let BITCOIN_ADDRESS_PUBKEYHASH_PREFIX = "1"
        let BITCOIN_ADDRESS_SCRIPTHASH_PREFIX = "3"
        let BITCOIN_ADDRESS_VALID_PREFIXES = [ BITCOIN_ADDRESS_PUBKEYHASH_PREFIX; BITCOIN_ADDRESS_SCRIPTHASH_PREFIX ]

        if (not (address.StartsWith(BITCOIN_ADDRESS_PUBKEYHASH_PREFIX))) &&
           (not (address.StartsWith(BITCOIN_ADDRESS_SCRIPTHASH_PREFIX))) then
            raise (AddressMissingProperPrefix(BITCOIN_ADDRESS_VALID_PREFIXES))

        if (address.Length > BITCOIN_MAX_ADDRESSES_LENGTH) then
            raise (AddressWithInvalidLength(BITCOIN_MAX_ADDRESSES_LENGTH))
        if (address.Length < BITCOIN_MIN_ADDRESSES_LENGTH) then
            raise (AddressWithInvalidLength(BITCOIN_MIN_ADDRESSES_LENGTH))

        // FIXME: add bitcoin checksum algorithm?
