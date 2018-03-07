namespace GWallet.Backend.Ether

open System
open System.Net
open System.Numerics
open System.Threading.Tasks

open Nethereum.Hex.HexTypes
open Nethereum.Web3
open Nethereum.RPC.Eth.DTOs

open GWallet.Backend

module Server =

    type ConnectionUnsuccessfulException =
        inherit Exception

        new(message: string, innerException: Exception) = { inherit Exception(message, innerException) }
        new(message: string) = { inherit Exception(message) }

    type ServerTimedOutException(message:string) =
       inherit ConnectionUnsuccessfulException (message)

    type ServerCannotBeResolvedException(message:string, innerException: Exception) =
       inherit ConnectionUnsuccessfulException (message, innerException)

    //let private PUBLIC_WEB3_API_ETH_INFURA = "https://mainnet.infura.io:8545" ?
    let private PUBLIC_WEB3_API_ETH_INFURA_MEW = "https://mainnet.infura.io/mew"
    let private PUBLIC_WEB3_API_ETH_MEW = "https://api.myetherapi.com/eth" // docs: https://www.myetherapi.com/

    // this below is https://classicetherwallet.com/'s public endpoint (TODO: to prevent having a SPOF, use https://etcchain.com/api/ too)
    let private PUBLIC_WEB3_API_ETC = "https://mewapi.epool.io"

    let private ethWeb3Infura = Web3(PUBLIC_WEB3_API_ETH_INFURA_MEW)
    let private ethWeb3Mew = Web3(PUBLIC_WEB3_API_ETH_MEW)
    let private etcWeb3 = Web3(PUBLIC_WEB3_API_ETC)

    // FIXME: we should randomize the result of this function, to mimic what we do in the bitcoin side
    let GetWeb3Servers (currency: Currency): list<Web3> =
        match currency with
        | Currency.ETC ->
            [ etcWeb3 ]
        | Currency.ETH ->
            [ ethWeb3Infura; ethWeb3Mew ]
        | _ -> failwith (sprintf "Assertion failed: Ether currency %s not supported?" (currency.ToString()))

    let exMsg = "Could not communicate with EtherServer"
    let WaitOnTask<'T,'R> (func: 'T -> Task<'R>) (arg: 'T) =
        let task = func arg
        let finished =
            try
                task.Wait Config.DEFAULT_NETWORK_TIMEOUT
            with
            | ex ->
                let maybeWebEx = FSharpUtil.FindException<WebException> ex
                match maybeWebEx with
                | None -> reraise()
                | Some(webEx) ->
                    if (webEx.Status = WebExceptionStatus.NameResolutionFailure) then
                        raise (ServerCannotBeResolvedException(exMsg, webEx))
                    reraise()
        if not finished then
            raise (ServerTimedOutException(exMsg))
        task.Result

    // we only have infura and mew for now, so requiring more than 1 would make it not fault tolerant...:
    let private NUMBER_OF_CONSISTENT_RESPONSES_TO_TRUST_ETH_SERVER_RESULTS = 1

    let private faultTolerantEthClient =
        FaultTolerantClient<ConnectionUnsuccessfulException> NUMBER_OF_CONSISTENT_RESPONSES_TO_TRUST_ETH_SERVER_RESULTS

    let private GetWeb3Funcs<'T,'R> (currency: Currency) (web3Func: Web3->'T->'R): list<'T->'R> =
        let servers = GetWeb3Servers currency
        let serverFuncs =
            List.map (fun (web3: Web3) ->
                          (fun (arg: 'T) ->
                                  web3Func web3 arg
                           )
                     )
                     servers
        serverFuncs

    let GetTransactionCount (currency: Currency) (address: string)
        : HexBigInteger =
        let web3Func (web3: Web3) (publicAddress: string): HexBigInteger =
            WaitOnTask web3.Eth.Transactions.GetTransactionCount.SendRequestAsync
                           publicAddress
        faultTolerantEthClient.Query<string,HexBigInteger>
            address
            (GetWeb3Funcs currency web3Func)

    let GetUnconfirmedBalance (currency: Currency) (address: string)
        : HexBigInteger =
        let web3Func (web3: Web3) (publicAddress: string): HexBigInteger =
            WaitOnTask web3.Eth.GetBalance.SendRequestAsync publicAddress
        faultTolerantEthClient.Query<string,HexBigInteger>
            address
            (GetWeb3Funcs currency web3Func)

    let private NUMBER_OF_CONFIRMATIONS_TO_CONSIDER_BALANCE_CONFIRMED = BigInteger(45)
    let private GetConfirmedBalanceInternal (web3: Web3) (publicAddress: string) =
        Task.Run(fun _ ->
            let latestBlockTask = web3.Eth.Blocks.GetBlockNumber.SendRequestAsync ()
            latestBlockTask.Wait()
            let latestBlock = latestBlockTask.Result
            let blockForConfirmationReference =
                BlockParameter(HexBigInteger(BigInteger.Subtract(latestBlock.Value,
                                                                 NUMBER_OF_CONFIRMATIONS_TO_CONSIDER_BALANCE_CONFIRMED)))
(*
            if (Config.DebugLog) then
                Console.Error.WriteLine (sprintf "Last block number and last confirmed block number: %s: %s"
                                                 (latestBlock.Value.ToString()) (blockForConfirmationReference.BlockNumber.Value.ToString()))
*)
            let balanceTask =
                web3.Eth.GetBalance.SendRequestAsync(publicAddress,blockForConfirmationReference)
            balanceTask.Wait()
            balanceTask.Result
        )

    let GetConfirmedBalance (currency: Currency) (address: string)
        : HexBigInteger =
        let web3Func (web3: Web3) (publicAddress: string): HexBigInteger =
            WaitOnTask (GetConfirmedBalanceInternal web3) publicAddress
        faultTolerantEthClient.Query<string,HexBigInteger>
            address
            (GetWeb3Funcs currency web3Func)

    let GetGasPrice (currency: Currency)
        : HexBigInteger =
        let web3Func (web3: Web3) (_: unit): HexBigInteger =
            WaitOnTask web3.Eth.GasPrice.SendRequestAsync ()
        faultTolerantEthClient.Query<unit,HexBigInteger>
            ()
            (GetWeb3Funcs currency web3Func)

    let BroadcastTransaction (currency: Currency) transaction
        : string =
        let web3Func (web3: Web3) (tx: string): string =
            WaitOnTask web3.Eth.Transactions.SendRawTransaction.SendRequestAsync tx
        faultTolerantEthClient.Query<string,string>
            transaction
            (GetWeb3Funcs currency web3Func)
