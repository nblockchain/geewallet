namespace GWallet.Backend.Ether

open System
open System.Threading.Tasks

open Nethereum.Hex.HexTypes
open Nethereum.Web3

open GWallet.Backend

module Server =

    type IWeb3 =
        abstract member GetTransactionCount: string -> Task<HexBigInteger>
        abstract member GetBalance: string -> Task<HexBigInteger>
        abstract member GetGasPrice: unit -> Task<HexBigInteger>
        abstract member BroadcastTransaction: string -> Task<string>

    type SomeWeb3(web3: Web3) =
        interface IWeb3 with
            member this.GetTransactionCount (publicAddress): Task<HexBigInteger> =
                web3.Eth.Transactions.GetTransactionCount.SendRequestAsync
                    publicAddress
            member this.GetBalance (publicAddress): Task<HexBigInteger> =
                web3.Eth.GetBalance.SendRequestAsync
                    publicAddress
            member this.GetGasPrice (): Task<HexBigInteger> =
                web3.Eth.GasPrice.SendRequestAsync ()
            member this.BroadcastTransaction transaction: Task<string> =
                web3.Eth.Transactions.SendRawTransaction.SendRequestAsync transaction

    //let private PUBLIC_WEB3_API_ETH_INFURA = "https://mainnet.infura.io:8545" ?
    let private PUBLIC_WEB3_API_ETH_INFURA_MEW = "https://mainnet.infura.io/mew"
    let private PUBLIC_WEB3_API_ETH_MEW = "https://api.myetherapi.com/eth" // docs: https://www.myetherapi.com/

    // this below is https://classicetherwallet.com/'s public endpoint (TODO: to prevent having a SPOF, use https://etcchain.com/api/ too)
    let private PUBLIC_WEB3_API_ETC = "https://mewapi.epool.io"

    let private ethWeb3Infura = SomeWeb3(Web3(PUBLIC_WEB3_API_ETH_INFURA_MEW)):>IWeb3
    let private ethWeb3Mew = SomeWeb3(Web3(PUBLIC_WEB3_API_ETH_MEW)):>IWeb3
    let private etcWeb3 = SomeWeb3(Web3(PUBLIC_WEB3_API_ETC)):>IWeb3

    let GetWeb3Servers (currency: Currency): list<IWeb3> =
        match currency with
        | Currency.ETC ->
            [ etcWeb3 ]
        | Currency.ETH ->
            [ ethWeb3Infura; ethWeb3Mew ]

    let private timeoutSpan = TimeSpan.FromSeconds(3.0)
    let WaitOnTask<'T,'R> (func: 'T -> Task<'R>) (arg: 'T) =
        let task = func arg
        let finished = task.Wait timeoutSpan
        if not finished then
            raise (TimeoutException(
                       sprintf "Couldn't get a response after %s seconds"
                               (timeoutSpan.TotalSeconds.ToString())))
        task.Result

    let private PlumbingCall<'T,'R> (currency: Currency)
                                    (arg: 'T)
                                    (web3Func: IWeb3 -> ('T -> Task<'R>))
                                    : 'R =
        let web3s = GetWeb3Servers currency
        let funcs =
            List.map (fun (web3: IWeb3) ->
                          fun (arg1: 'T) ->
                              WaitOnTask (fun (arg11:'T) -> web3Func web3 arg11) arg1)
                      web3s
        FaultTolerantClient.Query arg funcs

    let GetTransactionCount (currency: Currency) (address: string)
        : HexBigInteger =
        PlumbingCall currency address (fun web3 -> web3.GetTransactionCount)

    let GetBalance (currency: Currency) (address: string)
        : HexBigInteger =
        PlumbingCall currency address (fun web3 -> web3.GetBalance)

    let GetGasPrice (currency: Currency)
        : HexBigInteger =
        PlumbingCall currency () (fun web3 -> web3.GetGasPrice)

    let BroadcastTransaction (currency: Currency) transaction
        : string =
        PlumbingCall currency transaction (fun web3 -> web3.BroadcastTransaction)
