namespace GWallet.Backend.Ether

open System.Numerics

open Nethereum.Web3
open Nethereum.Hex.HexTypes
open Nethereum.Hex.HexConvertors.Extensions
open Nethereum.StandardTokenEIP20
open Nethereum.StandardTokenEIP20.ContractDefinition

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks

module TokenManager =

    let ContractAddresses: Map<Currency, string> =
        Map [
            Currency.DAI, "0x6B175474E89094C44Da98b954EedeAC495271d0F"
            Currency.SAI, "0x89d24A6b4CcB1B6fAA2625fE562bDD9a23260359"
        ]
    
    let TryGetCurrencyByContractAddress addr = 
        ContractAddresses
        |> Map.tryFindKey (fun _currency contractAddress -> contractAddress = addr)

    let GetTokenContractAddress currency =
        match currency with
        | Currency.DAI
        | Currency.SAI -> ContractAddresses.[currency]
        | _ -> raise <| invalidOp (SPrintF1 "%A has no contract address" currency)

    type TokenServiceWrapper(web3, currency: Currency) =
        inherit StandardTokenService(web3, GetTokenContractAddress currency)

        member self.ComposeInputDataForTransferTransaction (origin: string,
                                                            destination: string,
                                                            tokenAmountInWei: BigInteger,
                                                            gasLimit: BigInteger)
                                                           : string =
            let transferFuncBuilder = self.ContractHandler.GetFunction<TransferFunction>()

            let transferFunctionMsg = TransferFunction(To = destination,
                                                       Value = tokenAmountInWei)
            let tokenValue = HexBigInteger tokenAmountInWei
            let transactionInput = transferFuncBuilder.CreateTransactionInput(transferFunctionMsg,
                                                                              origin,
                                                                              HexBigInteger(gasLimit),
                                                                              tokenValue)
            if isNull transactionInput then
                failwith "Assertion failed: transaction input should not be null"
            if transactionInput.To <> GetTokenContractAddress currency then
                failwith "Assertion failed: transactionInput's TO property should be equal to the contract address"
            if not (transactionInput.Gas.Value.Equals(gasLimit)) then
                failwith "Assertion failed: transactionInput's GAS property should be equal to passed GasLimit parameter"
            if not (transactionInput.Value.Value.Equals(tokenAmountInWei)) then
                failwith "Assertion failed: transactionInput's VALUE property should be equal to passed tokenAmountInWei parameter"
            transactionInput.Data

        member self.DecodeInputDataForTransferTransaction (data: byte[]) =
            let transferFuncBuilder = self.ContractHandler.GetFunction<TransferFunction>()
            let decodedInput = transferFuncBuilder.DecodeInput (data.ToHex true)
            
            let expectedParamsCount = 2

            let transferDestination, transferAmountInWei =
                try
                    if decodedInput.Count = expectedParamsCount then
                        decodedInput.[0].Result :?> string,
                        decodedInput.[1].Result :?> BigInteger
                    else
                        failwith <| SPrintF2 "Invalid transfer function parameters count, expected %i got %i" expectedParamsCount decodedInput.Count
                with
                | :? System.InvalidCastException ->
                    failwith "Invalid transfer function parameters type"

            transferDestination, transferAmountInWei


    // this is a dummy instance we need in order to pass it to base class of StandardTokenService, but not
    // really used online; FIXME: propose "Web3-less" overload to Nethereum
    let private dummyOfflineWeb3 = Web3()
    type OfflineTokenServiceWrapper(currency: Currency) = 
        inherit TokenServiceWrapper(dummyOfflineWeb3, currency)
