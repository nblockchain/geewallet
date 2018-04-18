namespace GWallet.Backend

open System

open GWallet.Backend

module BlockExplorer =

    let GetTransactionHistory (account: IAccount): Uri =
        let baseUrl =
              match account.Currency with
              | Currency.BTC ->
                  // SmartBit explorer is built on top of NBitcoin: https://github.com/ProgrammingBlockchain/ProgrammingBlockchain/issues/1
                  "https://www.smartbit.com.au/address/"
              | Currency.LTC ->
                  // because the more popular https://live.blockcypher.com/ltc/ doesn't seem to have segwit support
                  "https://chainz.cryptoid.info/ltc/address.dws?"
              | Currency.ETH ->
                  // most popular one...
                  "https://etherscan.io/address/"
              | Currency.ETC ->
                  // the only one? minergate.com seems to only show blocks, not addresses
                  "https://gastracker.io/addr/"
              | Currency.DAI ->
                  sprintf "https://etherscan.io/token/%s?a=" Ether.TokenManager.DAI_CONTRACT_ADDRESS
        Uri(baseUrl + account.PublicAddress)

    let GetTransaction (currency: Currency) (txHash: string): Uri =
        let baseUrl =
              match currency with
              | Currency.BTC ->
                  "https://www.smartbit.com.au/tx/"
              | Currency.LTC ->
                  "https://chainz.cryptoid.info/ltc/tx.dws?"
              | Currency.ETH ->
                  "https://etherscan.io/tx/"
              | Currency.ETC ->
                  "https://gastracker.io/tx/"
              | Currency.DAI ->
                  "https://etherscan.io/tx/"
        Uri(baseUrl + txHash)
