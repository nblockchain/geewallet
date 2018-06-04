namespace GWallet.Backend.Ether

open System
open System.Net
open System.Numerics
open System.Threading.Tasks

open Nethereum.Util
open Nethereum.Hex.HexTypes
open Nethereum.Web3
open Nethereum.RPC.Eth.DTOs
open Nethereum.StandardTokenEIP20.Functions

open GWallet.Backend

module Server =

    type SomeWeb3(url: string) =
        inherit Web3(url)

        member val Url = url with get

    type ConnectionUnsuccessfulException =
        inherit Exception

        new(message: string, innerException: Exception) = { inherit Exception(message, innerException) }
        new(message: string) = { inherit Exception(message) }

    type ServerTimedOutException =
       inherit ConnectionUnsuccessfulException

       new(message: string, innerException: Exception) = { inherit ConnectionUnsuccessfulException(message, innerException) }
       new(message: string) = { inherit ConnectionUnsuccessfulException(message) }

    type ServerCannotBeResolvedException(message:string, innerException: Exception) =
       inherit ConnectionUnsuccessfulException (message, innerException)

    type ServerUnreachableException(message:string, innerException: Exception) =
        inherit ConnectionUnsuccessfulException (message, innerException)

    type ServerChannelNegotiationException(message:string, innerException: Exception) =
       inherit ConnectionUnsuccessfulException (message, innerException)

    type ServerMisconfiguredException(message:string, innerException: Exception) =
       inherit ConnectionUnsuccessfulException (message, innerException)

    type UnhandledWebException(status: WebExceptionStatus, innerException: Exception) =
       inherit Exception (sprintf "GWallet not prepared for this WebException with Status[%d]" (int status),
                          innerException)

    // https://en.wikipedia.org/wiki/List_of_HTTP_status_codes#Cloudflare
    type CloudFlareError =
        | ConnectionTimeOut = 522
        | WebServerDown = 521
        | OriginUnreachable = 523
        | OriginSslHandshakeError = 525

    //let private PUBLIC_WEB3_API_ETH_INFURA = "https://mainnet.infura.io:8545" ?
    let private ethWeb3Infura = SomeWeb3("https://mainnet.infura.io/mew")
    let private ethWeb3Mew = SomeWeb3("https://api.myetherapi.com/eth") // docs: https://www.myetherapi.com/

    // TODO: add the one from https://etcchain.com/api/ too
    let private etcWeb3ePoolIo = SomeWeb3("https://mewapi.epool.io")
    let private etcWeb3ZeroXInfraGeth = SomeWeb3("https://etc-geth.0xinfra.com")
    let private etcWeb3ZeroXInfraParity = SomeWeb3("https://etc-parity.0xinfra.com")
    let private etcWeb3CommonWealthGeth = SomeWeb3("https://etcrpc.viperid.online")
    // FIXME: the below one doesn't seem to work; we should include it anyway and make the algorithm discard it at runtime
    //let private etcWeb3CommonWealthMantis = SomeWeb3("https://etc-mantis.callisto.network")
    let private etcWeb3CommonWealthParity = SomeWeb3("https://etc-parity.callisto.network")

    let GetWeb3Servers (currency: Currency): list<SomeWeb3> =
        if currency = ETC then
            [
                etcWeb3CommonWealthParity;
                etcWeb3CommonWealthGeth;
                etcWeb3ePoolIo;
                etcWeb3ZeroXInfraParity;
                etcWeb3ZeroXInfraGeth;
            ]
        elif (currency.IsEthToken() || currency = Currency.ETH) then
            [ ethWeb3Mew; ethWeb3Infura ]
        else
            failwithf "Assertion failed: Ether currency %A not supported?" currency

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
                | None ->
                    let maybeHttpReqEx = FSharpUtil.FindException<Http.HttpRequestException> ex
                    match maybeHttpReqEx with
                    | None ->
                        let maybeRpcResponseEx =
                            FSharpUtil.FindException<Nethereum.JsonRpc.Client.RpcResponseException> ex
                        match maybeRpcResponseEx with
                        | None ->
                            reraise()
                        | Some rpcResponseEx ->
                            if (rpcResponseEx.Message.Contains "pruning=archive") then
                                raise (ServerMisconfiguredException(exMsg, rpcResponseEx))
                            reraise()
                    | Some(httpReqEx) ->
                        if (httpReqEx.Message.StartsWith(sprintf "%d " (int CloudFlareError.ConnectionTimeOut))) then
                            raise (ServerTimedOutException(exMsg, httpReqEx))
                        if (httpReqEx.Message.StartsWith(sprintf "%d " (int CloudFlareError.OriginUnreachable))) then
                            raise (ServerTimedOutException(exMsg, httpReqEx))
                        if (httpReqEx.Message.StartsWith(sprintf "%d " (int CloudFlareError.OriginSslHandshakeError))) then
                            raise (ServerChannelNegotiationException(exMsg, httpReqEx))
                        reraise()
                | Some(webEx) ->
                    if (webEx.Status = WebExceptionStatus.NameResolutionFailure) then
                        raise (ServerCannotBeResolvedException(exMsg, webEx))
                    if (webEx.Status = WebExceptionStatus.SecureChannelFailure) then
                        raise (ServerChannelNegotiationException(exMsg, webEx))
                    if (webEx.Status = WebExceptionStatus.ReceiveFailure) then
                        raise (ServerTimedOutException(exMsg, webEx))
                    raise (UnhandledWebException(webEx.Status, webEx))

        if not finished then
            raise (ServerTimedOutException(exMsg))
        task.Result

    // we only have infura and mew for now, so requiring more than 1 would make it not fault tolerant...:
    let private NUMBER_OF_CONSISTENT_RESPONSES_TO_TRUST_ETH_SERVER_RESULTS = 1
    let private NUMBER_OF_ALLOWED_PARALLEL_CLIENT_QUERY_JOBS = 2

    let private faultTolerantEthClient =
        FaultTolerantParallelClient<ConnectionUnsuccessfulException>(NUMBER_OF_CONSISTENT_RESPONSES_TO_TRUST_ETH_SERVER_RESULTS,
                                                             NUMBER_OF_ALLOWED_PARALLEL_CLIENT_QUERY_JOBS)

    let private GetWeb3Funcs<'T,'R> (currency: Currency) (web3Func: SomeWeb3->'T->'R): list<'T->'R> =
        let servers = GetWeb3Servers currency
        let serverFuncs =
            List.map (fun (web3: SomeWeb3) ->
                          (fun (arg: 'T) ->
                              try
                                  web3Func web3 arg
                              with
                              | :? ConnectionUnsuccessfulException ->
                                  reraise()
                              | ex ->
                                  raise (Exception(sprintf "Some problem when connecting to %s" web3.Url, ex))
                           )
                     )
                     servers
        serverFuncs

    let GetTransactionCount (currency: Currency) (address: string)
                                : Async<HexBigInteger> =
        async {
            let web3Func (web3: Web3) (publicAddress: string): HexBigInteger =
                WaitOnTask web3.Eth.Transactions.GetTransactionCount.SendRequestAsync
                               publicAddress
            return! faultTolerantEthClient.Query<string,HexBigInteger>
                address
                (GetWeb3Funcs currency web3Func)
        }

    let GetUnconfirmedEtherBalance (currency: Currency) (address: string)
                                       : Async<HexBigInteger> =
        async {
            let web3Func (web3: Web3) (publicAddress: string): HexBigInteger =
                WaitOnTask web3.Eth.GetBalance.SendRequestAsync publicAddress
            return! faultTolerantEthClient.Query<string,HexBigInteger>
                address
                (GetWeb3Funcs currency web3Func)
        }

    let GetUnconfirmedTokenBalance (currency: Currency) (address: string): Async<BigInteger> =
        async {
            let web3Func (web3: Web3) (publicAddress: string): BigInteger =
                let tokenService = TokenManager.DaiContract web3
                WaitOnTask tokenService.GetBalanceOfAsync publicAddress
            return! faultTolerantEthClient.Query<string,BigInteger>
                address
                (GetWeb3Funcs currency web3Func)
        }

    let private NUMBER_OF_CONFIRMATIONS_TO_CONSIDER_BALANCE_CONFIRMED = BigInteger(45)
    let private GetConfirmedEtherBalanceInternal (web3: Web3) (publicAddress: string) =
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

    let GetConfirmedEtherBalance (currency: Currency) (address: string)
                                     : Async<HexBigInteger> =
        async {
            let web3Func (web3: Web3) (publicAddress: string): HexBigInteger =
                WaitOnTask (GetConfirmedEtherBalanceInternal web3) publicAddress
            return! faultTolerantEthClient.Query<string,HexBigInteger>
                        address
                        (GetWeb3Funcs currency web3Func)
        }

    let private GetConfirmedTokenBalanceInternal (web3: Web3) (publicAddress: string): Task<BigInteger> =
        let balanceFunc(): Task<BigInteger> =
            let latestBlockTask = web3.Eth.Blocks.GetBlockNumber.SendRequestAsync ()
            latestBlockTask.Wait()
            let latestBlock = latestBlockTask.Result
            let blockForConfirmationReference =
                BlockParameter(HexBigInteger(BigInteger.Subtract(latestBlock.Value,
                                                                 NUMBER_OF_CONFIRMATIONS_TO_CONSIDER_BALANCE_CONFIRMED)))
            let balanceOfFunctionMsg = TokenManager.BalanceOfFunctionFromErc20TokenContract publicAddress

            let contractHandler = web3.Eth.GetContractHandler(TokenManager.DAI_CONTRACT_ADDRESS)
            contractHandler.QueryAsync<TokenManager.BalanceOfFunctionFromErc20TokenContract,BigInteger>
                                    (balanceOfFunctionMsg,
                                     blockForConfirmationReference)
        Task.Run<BigInteger> balanceFunc

    let GetConfirmedTokenBalance (currency: Currency) (address: string): Async<BigInteger> =
        async {
            let web3Func (web3: Web3) (publicddress: string): BigInteger =
                WaitOnTask (GetConfirmedTokenBalanceInternal web3) address
            return! faultTolerantEthClient.Query<string,BigInteger>
                        address
                        (GetWeb3Funcs currency web3Func)
        }

    let EstimateTokenTransferFee (account: IAccount) (amount: decimal) destination
                                     : Async<HexBigInteger> =
        async {
            let web3Func (web3: Web3) (_: unit): HexBigInteger =
                let contractHandler = web3.Eth.GetContractHandler(TokenManager.DAI_CONTRACT_ADDRESS)
                let amountInWei = UnitConversion.Convert.ToWei(amount, UnitConversion.EthUnit.Ether)
                let transferFunctionMsg = TransferFunction(FromAddress = account.PublicAddress,
                                                           To = destination,
                                                           TokenAmount = amountInWei)
                WaitOnTask contractHandler.EstimateGasAsync transferFunctionMsg
            return! faultTolerantEthClient.Query<unit,HexBigInteger>
                        ()
                        (GetWeb3Funcs account.Currency web3Func)
        }

    let GetGasPrice (currency: Currency)
        : Async<HexBigInteger> =
        async {
            let web3Func (web3: Web3) (_: unit): HexBigInteger =
                WaitOnTask web3.Eth.GasPrice.SendRequestAsync ()
            return! faultTolerantEthClient.Query<unit,HexBigInteger>
                        ()
                        (GetWeb3Funcs currency web3Func)
        }

    let BroadcastTransaction (currency: Currency) transaction
        : Async<string> =
        let insufficientFundsMsg = "Insufficient funds"

        // UPDATE/FIXME: can't use reraise inside async, blergh! https://stackoverflow.com/questions/7168801/how-to-use-reraise-in-async-workflows-in-f
        let reraiseWorkAround ex =
            Exception("Unhandled exception when trying to broadcast transaction", ex)

        async {
            let web3Func (web3: Web3) (tx: string): string =
                WaitOnTask web3.Eth.Transactions.SendRawTransaction.SendRequestAsync tx
            try
                return! faultTolerantEthClient.Query<string,string>
                            transaction
                            (GetWeb3Funcs currency web3Func)
            with
            | ex ->
                match FSharpUtil.FindException<Nethereum.JsonRpc.Client.RpcResponseException> ex with
                | None ->
                    return raise (reraiseWorkAround ex)
                | Some rpcResponseException ->
                    // FIXME: this is fragile, ideally should respond with an error code
                    if rpcResponseException.Message.StartsWith(insufficientFundsMsg,
                                                               StringComparison.InvariantCultureIgnoreCase) then
                        raise InsufficientFunds
                    return raise (reraiseWorkAround ex)
                return raise (reraiseWorkAround ex)
        }
