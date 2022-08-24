namespace GWallet.Backend.UtxoCoin.Lightning

open System.Linq

open NBitcoin
open DotNetLightning.Utils

open GWallet.Backend
open GWallet.Backend.UtxoCoin
open GWallet.Backend.UtxoCoin.Account

module internal FeeManagement =
    // BEWARE: should not be used if the target address is not ourselves (due to change from fee UTXOs)
    let AddFeeInputsWithCpfpSupport (account: IAccount, password: string) (childTx: TransactionBuilder) (parentTxInfoOpt: Option<Transaction * ScriptCoin>) =
        async {
            let privateKey = Account.GetPrivateKey (account :?> NormalUtxoAccount) password

            let! feeRate = async {
                let! feeEstimator = FeeEstimator.Create account.Currency
                return feeEstimator.FeeRatePerKw
            }

            let job =
                Account.GetElectrumScriptHashFromPublicAddress account.Currency account.PublicAddress
                |> ElectrumClient.GetUnspentTransactionOutputs
            let! utxos = Server.Query account.Currency (QuerySettings.Default ServerSelectionMode.Fast) job None

            if not (utxos.Any()) then
                return raise InsufficientFunds
            let possibleInputs =
                seq {
                    for utxo in utxos do
                        yield {
                            TransactionId = utxo.TxHash
                            OutputIndex = utxo.TxPos
                            Value = utxo.Value
                        }
                }

            // first ones are the smallest ones
            let inputsOrderedByAmount = possibleInputs.OrderBy(fun utxo -> utxo.Value) |> List.ofSeq

            childTx.AddKeys privateKey |> ignore<TransactionBuilder>

            let deltaParentTxFee =
                parentTxInfoOpt
                |> Option.map (fun (parentTx, spentCoin) ->
                    let requiredParentTxFee = feeRate.AsNBitcoinFeeRate().GetFee parentTx
                    let actualParentTxFee =
                        Array.singleton (spentCoin :> ICoin)
                        |> parentTx.GetFee
                    requiredParentTxFee - actualParentTxFee    
                ) |> Option.defaultValue Money.Zero

            let rec addUtxoForChildFee unusedUtxos =
                async {
                    try
                        let fees = childTx.EstimateFees (feeRate.AsNBitcoinFeeRate())
                        return fees, unusedUtxos
                    with
                    | :? NBitcoin.NotEnoughFundsException as _ex ->
                        match unusedUtxos with
                        | [] -> return raise InsufficientFunds
                        | head::tail ->
                            let! newInput = head |> ConvertToInputOutpointInfo account.Currency
                            let newCoin = newInput |> ConvertToICoin (account :?> IUtxoAccount)
                            childTx.AddCoin newCoin |> ignore<TransactionBuilder>
                            return! addUtxoForChildFee tail
                }

            let rec addUtxosForParentFeeAndFinalize unusedUtxos =
                async {
                    try
                        return childTx.BuildTransaction true
                    with
                    | :? NBitcoin.NotEnoughFundsException as _ex ->
                        match unusedUtxos with
                        | [] -> return raise InsufficientFunds
                        | head::tail ->
                            let! newInput = head |> ConvertToInputOutpointInfo account.Currency
                            let newCoin = newInput |> ConvertToICoin (account :?> IUtxoAccount)
                            childTx.AddCoin newCoin |> ignore<TransactionBuilder>
                            return! addUtxosForParentFeeAndFinalize tail
                }

            let! childFee, unusedUtxos = addUtxoForChildFee inputsOrderedByAmount
            childTx.SendFees (childFee + deltaParentTxFee) |> ignore<TransactionBuilder>
            let! transaction = addUtxosForParentFeeAndFinalize unusedUtxos

            return transaction, childFee + deltaParentTxFee
        }
