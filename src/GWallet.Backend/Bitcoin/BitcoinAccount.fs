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

    let EstimateFee account amount =
        let electrumServer = ElectrumServer.PickRandom()
        use electrumClient = new ElectrumClient(electrumServer)
        let utxos = electrumClient.GetUnspentTransactionOutputs (account:>IAccount).PublicAddress
        let inputs =
            seq {
                for utxo in utxos do
                    let transRaw = electrumClient.GetBlockchainTransaction utxo.TxHash
                    let inputTx = Transaction(transRaw)
                    yield inputTx,utxo.TxPos,utxo.Value
            } |> List.ofSeq
        let transactionDraft = Transaction()
        for inputTx,index,value in inputs do
            let inputAdded = transactionDraft.AddInput(inputTx, index)
            // see https://github.com/MetacoSA/NBitcoin/pull/261
            inputAdded.ScriptSig <- inputTx.Outputs.ElementAt(index).ScriptPubKey
        let dummyAddressForDraftTx = "1KsFhYKLs8qb1GHqrPxHoywNQpet2CtP9t"
        let destAddress = BitcoinAddress.Create(dummyAddressForDraftTx, Network.Main)
        let sumOfInputValues = inputs.Sum(fun (_,_,value) -> value)
        let txOutDraft = TxOut(Money(sumOfInputValues), destAddress)
        transactionDraft.Outputs.Add(txOutDraft)
        let transactionSizeInBytes = (transactionDraft.ToBytes().Length)
        let estimatedFinalTransSize = (BYTES_PER_INPUT_ESTIMATION_CONSTANT * (inputs.Length)) + transactionSizeInBytes
        let btcPerKiloByteForFastTrans = electrumClient.EstimateFee 2 //querying for 1 will always return -1 surprisingly...
        MinerFee(estimatedFinalTransSize, btcPerKiloByteForFastTrans, DateTime.Now, transactionDraft)

    let SendPayment (account: NormalAccount) (destination: string) (amount: decimal)
                    (password: string)
                    (minerFee: MinerFee)
                    =
        let transaction = minerFee.DraftTransaction
        transaction.Outputs.Remove(transaction.Outputs.[0]) |> ignore
        if (transaction.Outputs.Count > 0) then
            failwith "Draft transaction should only contain one output"
        let destAddress = BitcoinAddress.Create(destination, Network.Main)
        let amountInSatoshis = amount * decimal 100000000
        let outputAmount = Money(Convert.ToInt64(amountInSatoshis))

        let txOutAfterRemovingFee = TxOut(outputAmount, destAddress)
        transaction.Outputs.Add(txOutAfterRemovingFee)
        let encryptedPrivateKey = File.ReadAllText(account.AccountFile.FullName)
        let encryptedSecret = BitcoinEncryptedSecretNoEC(encryptedPrivateKey, Network.Main)

        let privateKey =
            try
                encryptedSecret.GetKey(password)
            with
            | :? SecurityException ->
                raise (InvalidPassword)

        transaction.Sign(privateKey, false)
        let transSizeAfterSigning = transaction.ToBytes().Length
        Console.WriteLine (sprintf "Transaction size after signing: %d bytes" transSizeAfterSigning)
        if (Math.Abs(transSizeAfterSigning - minerFee.EstimatedTransactionSizeInBytes) > 2) then
            failwith (sprintf "Transaction size estimation failed, got %d but calculated %d bytes (a difference of %d, with %d inputs)"
                              transSizeAfterSigning minerFee.EstimatedTransactionSizeInBytes
                              (transSizeAfterSigning - minerFee.EstimatedTransactionSizeInBytes)
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
