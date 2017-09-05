namespace GWallet.Backend.Bitcoin

// NOTE: we can rename this file to less redundant "Account.fs" when this F# compiler bug is fixed:
// https://github.com/Microsoft/visualfsharp/issues/3231

open System
open System.IO
open System.Security
open System.Linq

open NBitcoin

open GWallet.Backend

module internal Account =

    let GetPublicAddressFromAccountFile (accountFile: FileInfo) =
        let pubKey = new PubKey(accountFile.Name)
        pubKey.GetAddress(Network.Main).ToString()

    // TODO: return MaybeCached<decimal>
    let GetBalance(account: IAccount): decimal =
        let electrumServer = ElectrumServer.PickRandom()
        use electrumClient = new ElectrumClient(electrumServer)
        electrumClient.GetBalance account.PublicAddress |> UnitConversion.FromSatoshiToBTC

    // this is a rough guess between 3 tests with 1, 2 and 3 inputs:
    // 1  -> 81, 2 -> 163, 3 -> 243  FIXME: anyway I should use NBitcoin's estimation facilicities
    let private BYTES_PER_INPUT_ESTIMATION_CONSTANT = 81

    let EstimateFee account (amount: decimal) =
        let rec addInputsUntilAmount (inputs: list<Transaction*int*Int64>)
                                      soFarInSatoshis
                                     (transDraft: Transaction)
                                      amount =
            match inputs with
            | [] ->
                failwith "Not enough funds"
            | (tx,index,value)::tail ->
                let inputAdded = transDraft.AddInput(tx, index)
                // see https://github.com/MetacoSA/NBitcoin/pull/261
                inputAdded.ScriptSig <- tx.Outputs.ElementAt(index).ScriptPubKey

                let newSoFar = soFarInSatoshis + value
                if (newSoFar < amount) then
                    addInputsUntilAmount tail newSoFar transDraft amount
                else
                    newSoFar

        let electrumServer = ElectrumServer.PickRandom()
        use electrumClient = new ElectrumClient(electrumServer)
        let utxos = electrumClient.GetUnspentTransactionOutputs (account:>IAccount).PublicAddress
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
        let totalValueOfInputs = addInputsUntilAmount inputsOrderedByAmount 0L transactionDraft amountInSatoshis

        let dummyAddressForDraftTx = "1KsFhYKLs8qb1GHqrPxHoywNQpet2CtP9t"
        let destAddress = BitcoinAddress.Create(dummyAddressForDraftTx, Network.Main)

        let txMainOutDraft = TxOut(Money(amountInSatoshis), destAddress)
        transactionDraft.Outputs.Add(txMainOutDraft)

        if (amountInSatoshis <> totalValueOfInputs) then
            let originAddress = BitcoinAddress.Create(dummyAddressForDraftTx, Network.Main)
            let changeAmount = totalValueOfInputs - amountInSatoshis
            let txChangeOutDraft = TxOut(Money(changeAmount), originAddress)
            transactionDraft.Outputs.Add(txChangeOutDraft)

        let transactionSizeInBytes = (transactionDraft.ToBytes().Length)
        let estimatedFinalTransSize =
            (BYTES_PER_INPUT_ESTIMATION_CONSTANT * (transactionDraft.Inputs.Count)) + transactionSizeInBytes
        let btcPerKiloByteForFastTrans = electrumClient.EstimateFee 2 //querying for 1 will always return -1 surprisingly...
        MinerFee(estimatedFinalTransSize, btcPerKiloByteForFastTrans, DateTime.Now, transactionDraft)

    let SendPayment (account: NormalAccount) (destination: string) (amount: decimal)
                    (password: string)
                    (btcMinerFee: MinerFee)
                    =
        let transaction = btcMinerFee.DraftTransaction
        let minerFee = btcMinerFee :> IBlockchainFee
        let destAddress = BitcoinAddress.Create(destination, Network.Main)
        let sourceAddress = BitcoinAddress.Create((account:>IAccount).PublicAddress, Network.Main)
        match transaction.Outputs.Count with
        | 1 -> // it means we're sending all balance!
            transaction.Outputs.Remove(transaction.Outputs.[0]) |> ignore
            let amountInSatoshis = amount * 100000000m
            let outputAmount = Money(Convert.ToInt64(amountInSatoshis))

            let txOutAfterRemovingFee = TxOut(outputAmount, destAddress)
            transaction.Outputs.Add(txOutAfterRemovingFee)
        | 2 -> // it means there's change involved (send change back to change-address)
            for output in transaction.Outputs.ToArray() do
                let removed = transaction.Outputs.Remove(output)
                if not removed then
                    failwith "Failed to remove draft output"

                if (output.Value.ToDecimal(MoneyUnit.BTC) <> amount) then
                    let minerFeeInSatoshis = Convert.ToInt64(minerFee.Value * 100000000m)
                    let changeAmount = Money(output.Value.Satoshi - minerFeeInSatoshis)
                    let changeOuput = TxOut(changeAmount, sourceAddress)
                    transaction.Outputs.Add(changeOuput)
                else
                    // to remove the dummy recipient address and use the right one
                    transaction.Outputs.Add(TxOut(output.Value, destAddress))
        | unexpectedCount ->
            failwith (sprintf "Assert: count of transactionDraft shouldn't have been %d" unexpectedCount)

        let encryptedPrivateKey = File.ReadAllText(account.AccountFile.FullName)
        let encryptedSecret = BitcoinEncryptedSecretNoEC(encryptedPrivateKey, Network.Main)

        let privateKey =
            try
                encryptedSecret.GetKey(password)
            with
            | :? SecurityException ->
                raise (InvalidPassword)

        let transCheckResultBeforeSigning = transaction.Check()
        if (transCheckResultBeforeSigning <> TransactionCheckResult.Success) then
            failwith (sprintf "Transaction check failed before signing with %A" transCheckResultBeforeSigning)
        transaction.Sign(privateKey, false)
        let transCheckResultAfterSigning = transaction.Check()
        if (transCheckResultAfterSigning <> TransactionCheckResult.Success) then
            failwith (sprintf "Transaction check failed after signing with %A" transCheckResultAfterSigning)

        let transSizeAfterSigning = transaction.ToBytes().Length
        Console.WriteLine (sprintf "Transaction size after signing: %d bytes" transSizeAfterSigning)
        if (Math.Abs(transSizeAfterSigning - btcMinerFee.EstimatedTransactionSizeInBytes) > 2) then
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
        let BITCOIN_ADDRESSES_LENGTH = 34
        let BITCOIN_ADDRESS_PREFIX = "1"

        if not (address.StartsWith(BITCOIN_ADDRESS_PREFIX)) then
            raise (AddressMissingProperPrefix(BITCOIN_ADDRESS_PREFIX))

        if (address.Length <> BITCOIN_ADDRESSES_LENGTH) then
            raise (AddressWithInvalidLength(BITCOIN_ADDRESSES_LENGTH))

        // FIXME: add bitcoin checksum algorithm?
