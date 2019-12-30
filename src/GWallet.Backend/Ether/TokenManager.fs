namespace GWallet.Backend.Ether

open System.Numerics

open Nethereum.Web3
open Nethereum.Hex.HexTypes
open Nethereum.StandardTokenEIP20
open Nethereum.StandardTokenEIP20.ContractDefinition

module TokenManager =

    let internal SAI_CONTRACT_ADDRESS = "0x89d24A6b4CcB1B6fAA2625fE562bDD9a23260359"
    type DaiContract(web3) =
        inherit StandardTokenService(web3, SAI_CONTRACT_ADDRESS)

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
            if (transactionInput = null) then
                failwith "Assertion failed: transaction input should not be null"
            if transactionInput.To <> SAI_CONTRACT_ADDRESS then
                failwith "Assertion failed: transactionInput's TO property should be equal to DAI's contract address"
            if not (transactionInput.Gas.Value.Equals(gasLimit)) then
                failwith "Assertion failed: transactionInput's GAS property should be equal to passed GasLimit parameter"
            if not (transactionInput.Value.Value.Equals(tokenAmountInWei)) then
                failwith "Assertion failed: transactionInput's VALUE property should be equal to passed tokenAmountInWei parameter"
            transactionInput.Data

    // this is a dummy instance we need in order to pass it to base class of StandardTokenService, but not
    // really used online; FIXME: propose "Web3-less" overload to Nethereum
    let private dummyOfflineWeb3 = Web3()
    let internal OfflineDaiContract = DaiContract(dummyOfflineWeb3)
