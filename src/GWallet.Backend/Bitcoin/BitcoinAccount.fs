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

    // this is a rough guess between 3 tests with 1, 2 and 3 inputs:
    // 1input  -> 215(total): 83+(X*1)
    // 2inputs -> 386(total): 124+(X*2)
    // 3inputs -> 559(total): 165+(X*3)  ... therefore X = 131?
    // FIXME: anyway I should use NBitcoin's estimation facilicities
    let private BYTES_PER_INPUT_ESTIMATION_CONSTANT = 131

    let EstimateFee account (amount: decimal) (destination: string) =
        let rec addInputsUntilAmount (inputs: list<Transaction*int*Int64>)
                                      soFarInSatoshis
                                      amount
                                     (acc: list<TransactionOutpoint>)
                                     : list<TransactionOutpoint>*int64 =
            match inputs with
            | [] ->
                failwith (sprintf "Not enough funds (needed: %s, got so far: %s)"
                                  (amount.ToString()) (soFarInSatoshis.ToString()))
            | (tx,index,value)::tail ->
                let input = { Transaction = tx; OutputIndex = index }
                let newAcc = input::acc

                let newSoFar = soFarInSatoshis + value
                if (newSoFar < amount) then
                    addInputsUntilAmount tail newSoFar amount newAcc
                else
                    newAcc,newSoFar

        let electrumServer = ElectrumServer.PickRandom()
        use electrumClient = new ElectrumClient(electrumServer)
        let utxos = electrumClient.GetUnspentTransactionOutputs (account:>IAccount).PublicAddress
        if not (utxos.Any()) then
            failwith "No UTXOs found!"
        let inputs =
            seq {
                for utxo in utxos do
                    let transRaw = electrumClient.GetBlockchainTransaction utxo.TxHash
                    let inputTx = Transaction(transRaw)
                    yield inputTx,utxo.TxPos,utxo.Value
            }
        // first ones are the smallest ones
        let inputsOrderedByAmount = inputs.OrderBy(fun (_,_,value) -> value) |> List.ofSeq

        let transactionDraft = Transaction()
        let amountInSatoshis = Convert.ToInt64(amount * 100000000m)
        let inputsToUse,totalValueOfInputs =
            addInputsUntilAmount inputsOrderedByAmount 0L amountInSatoshis []

        for input in inputsToUse do
            transactionDraft.AddInput(input.Transaction, input.OutputIndex) |> ignore

        let destAddress = BitcoinAddress.Create(destination, Network.Main)

        let txMainOutDraft = TxOut(Money(amountInSatoshis), destAddress)
        transactionDraft.Outputs.Add(txMainOutDraft)

        if (amountInSatoshis <> totalValueOfInputs) then
            let originAddress = BitcoinAddress.Create((account:>IAccount).PublicAddress, Network.Main)
            let changeAmount = totalValueOfInputs - amountInSatoshis
            let txChangeOutDraft = TxOut(Money(changeAmount), originAddress)
            transactionDraft.Outputs.Add(txChangeOutDraft)

        let transactionSizeInBytes = (transactionDraft.ToBytes().Length)
        //Console.WriteLine("transactionSize in bytes before signing: " + transactionSizeInBytes.ToString())
        let numberOfInputs = transactionDraft.Inputs.Count
        let estimatedFinalTransSize = transactionSizeInBytes +
            (BYTES_PER_INPUT_ESTIMATION_CONSTANT * transactionDraft.Inputs.Count) +
            (numberOfInputs - 1)
        let btcPerKiloByteForFastTrans = electrumClient.EstimateFee 2 //querying for 1 will always return -1 surprisingly...

        let coins = inputsToUse.Select(fun input -> input.ToCoin())
        MinerFee(estimatedFinalTransSize, btcPerKiloByteForFastTrans, DateTime.Now, transactionDraft, coins)

    let SendPayment (account: NormalAccount) (destination: string) (amount: TransferAmount)
                    (password: string)
                    (btcMinerFee: MinerFee)
                    =
        let transaction = btcMinerFee.DraftTransaction
        let minerFee = btcMinerFee :> IBlockchainFee
        let minerFeeInSatoshis = Convert.ToInt64(minerFee.Value * 100000000m)
        let amountInSatoshis = Convert.ToInt64(amount.ValueToSend * 100000000m)
        let destAddress = BitcoinAddress.Create(destination, Network.Main)
        let sourceAddress = BitcoinAddress.Create((account:>IAccount).PublicAddress, Network.Main)

        // it means we're sending all balance!
        if (amount.IdealValueRemainingAfterSending = 0.0m) then
            if (transaction.Outputs.Count <> 1) then
                failwith "Assertion outputsCount==1 failed (it was %M)"

            transaction.Outputs.Remove(transaction.Outputs.[0]) |> ignore

            let outputAmount = Money(amountInSatoshis - minerFeeInSatoshis)
            let txOutAfterRemovingFee = TxOut(outputAmount, destAddress)
            transaction.Outputs.Add(txOutAfterRemovingFee)

        else // it means there's change involved (send change back to change-address)
            if (transaction.Outputs.Count <> 2) then
                failwith "Assertion outputsCount==2 failed (it was %M)"

            let changeOutput =
                transaction.Outputs.ToArray()
                                   .Single(fun o -> amountInSatoshis <> o.Value.Satoshi)

            let removed = transaction.Outputs.Remove(changeOutput)
            if not removed then
                failwith "Failed to remove draft output"
            let changeAmount = Money(changeOutput.Value.Satoshi - minerFeeInSatoshis)
            let changeOuput = TxOut(changeAmount, sourceAddress)
            transaction.Outputs.Add(changeOuput)

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
            btcMinerFee.CoinsToSign.Select(fun c -> c.ToScriptCoin(privateKey.PubKey.WitHash.ScriptPubKey) :> ICoin)
            |> Seq.toArray

        let transCheckResultBeforeSigning = transaction.Check()
        if (transCheckResultBeforeSigning <> TransactionCheckResult.Success) then
            failwith (sprintf "Transaction check failed before signing with %A" transCheckResultBeforeSigning)
        transaction.Sign(privateKey, coinsToSign)
        let transCheckResultAfterSigning = transaction.Check()
        if (transCheckResultAfterSigning <> TransactionCheckResult.Success) then
            failwith (sprintf "Transaction check failed after signing with %A" transCheckResultAfterSigning)

        let maxDeviationAllowedForEstimationToNotBeConsideredAnError = 2
        let transSizeAfterSigning = transaction.ToBytes().Length
        //Console.WriteLine (sprintf "Transaction size after signing: %d bytes" transSizeAfterSigning)
        let differenceBetweenRealSizeAndEstimated = transSizeAfterSigning - btcMinerFee.EstimatedTransactionSizeInBytes
        if (Math.Abs(differenceBetweenRealSizeAndEstimated) > maxDeviationAllowedForEstimationToNotBeConsideredAnError) then
            failwith (sprintf "Transaction size estimation failed, got %d but calculated %d bytes (a difference of %d, with %d inputs)"
                              transSizeAfterSigning btcMinerFee.EstimatedTransactionSizeInBytes
                              (transSizeAfterSigning - btcMinerFee.EstimatedTransactionSizeInBytes)
                              transaction.Inputs.Count)
        let electrumServer = ElectrumServer.PickRandom()
        use electrumClient = new ElectrumClient(electrumServer)
        let newTxId = electrumClient.BroadcastTransaction (transaction.ToHex())
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
