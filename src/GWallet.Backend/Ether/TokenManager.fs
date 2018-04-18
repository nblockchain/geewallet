namespace GWallet.Backend.Ether

open System.Numerics
open System.Linq

open Nethereum.Web3
open Nethereum.Hex.HexTypes
open Nethereum.Contracts
open Nethereum.Contracts.CQS
open Nethereum.StandardTokenEIP20
open Nethereum.ABI.FunctionEncoding.Attributes
open Nethereum.StandardTokenEIP20.Functions

module TokenManager =

    let internal DAI_CONTRACT_ADDRESS = "0x89d24A6b4CcB1B6fAA2625fE562bDD9a23260359"
    type DaiContract(web3) =
        inherit StandardTokenService(web3, DAI_CONTRACT_ADDRESS)

        member this.ComposeInputDataForTransferTransaction (origin: string,
                                                            destination: string,
                                                            tokenAmountInWei: BigInteger,
                                                            gasLimit: BigInteger)
                                                           : string =
            let contractBuilder = base.Contract.ContractBuilder
            let contractAbi = contractBuilder.ContractABI
            let transferFunc = contractAbi.Functions.FirstOrDefault(fun func -> func.Name = "transfer")
            if (transferFunc = null) then
                failwith "Transfer function not found?"
            let transferFuncBuilder = FunctionBuilder<TransferFunction>(contractBuilder, transferFunc)

            let transferFunctionMsg = TransferFunction(FromAddress = origin,
                                                       To = destination,
                                                       TokenAmount = tokenAmountInWei)
            let tokenValue = HexBigInteger tokenAmountInWei
            let transactionInput = transferFuncBuilder.CreateTransactionInput(transferFunctionMsg,
                                                                              origin,
                                                                              HexBigInteger(gasLimit),
                                                                              tokenValue)
            if (transactionInput = null) then
                failwith "Assertion failed: transaction input should not be null"
            if (transactionInput.To <> DAI_CONTRACT_ADDRESS) then
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

    // FIXME: remove this once it gets included in Nethereum, same as Transfer is already there:
    // https://github.com/Nethereum/Nethereum/blob/master/src/Nethereum.StandardTokenEIP20/Functions/TransferFunction.cs
    [<Function("balanceOf", "uint256")>]
    type BalanceOfFunctionFromErc20TokenContract(owner: string) =
        inherit ContractMessage()

        [<Parameter("address", "_owner", 1)>]
        member val Owner = owner with get
