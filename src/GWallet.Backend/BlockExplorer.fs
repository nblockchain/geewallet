namespace GWallet.Backend

open System

open GWallet.Backend
open GWallet.Backend.Ether
open GWallet.Backend.FSharpUtil.UwpHacks

module BlockExplorer =

    let GetTransactionHistory (account: IAccount): Uri =
        let baseUrl =
              match account.Currency with
              | Currency.BTC ->
                  "https://mempool.space/address/"
              | Currency.LTC ->
                  "https://litecoinspace.org/address/"
              | Currency.ETH ->
                  // most popular one...
                  "https://etherscan.io/address/"
              | Currency.ETC ->
                  // maybe blockscout is better? minergate.com seems to only show blocks, not addresses
                  "https://etcblockexplorer.com/address/addr/"
              | Currency.DAI ->
                  SPrintF1 "https://etherscan.io/token/%s?a=" (TokenManager.GetTokenContractAddress account.Currency)
        Uri(baseUrl + account.PublicAddress)

    let GetTransaction (currency: Currency) (txHash: string): Uri =
        let baseUrl =
              match currency with
              | Currency.BTC ->
                  "https://mempool.space/tx/"
              | Currency.LTC ->
                  "https://litecoinspace.org/tx/"
              | Currency.ETH ->
                  "https://etherscan.io/tx/"
              | Currency.ETC ->
                  "https://etcblockexplorer.com/tx/"
              | Currency.DAI ->
                  "https://etherscan.io/tx/"
        Uri(baseUrl + txHash)
