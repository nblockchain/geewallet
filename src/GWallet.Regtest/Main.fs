open System
open NBitcoin

open DotNetLightning.Utils
open GWallet.Regtest
open GWallet.Backend.FSharpUtil.UwpHacks

let rec keepSlowlyMining<'T> (bitcoind: Bitcoind) (lndAddress: BitcoinAddress): Async<'T> = async {
    do! Async.Sleep 3000
    bitcoind.GenerateBlocks (BlockHeightOffset32 1u) lndAddress
    return! keepSlowlyMining bitcoind lndAddress
}

let mainJob (walletAddress: BitcoinAddress): Async<unit> = async {
    use bitcoind = Bitcoind.Start()
    use _electrumServer = ElectrumServer.Start bitcoind
    use! lnd = Lnd.Start bitcoind
    
    // Geewallet cannot use coinbase outputs. To work around that we mine a
    // block to a LND instance and afterwards tell it to send funds to the
    // geewallet instance
    let! lndAddress = lnd.GetDepositAddress()
    let blocksMinedToLnd = BlockHeightOffset32 1u
    bitcoind.GenerateBlocks blocksMinedToLnd lndAddress

    let maturityDurationInNumberOfBlocks = BlockHeightOffset32 (uint32 NBitcoin.Consensus.RegTest.CoinbaseMaturity)
    bitcoind.GenerateBlocks maturityDurationInNumberOfBlocks lndAddress

    // We confirm the one block mined to LND, by waiting for LND to see the chain
    // at a height which has that block matured. The height at which the block will
    // be matured is 100 on regtest. Since we initialally mined one block for LND,
    // this will wait until the block height of LND reaches 1 (initial blocks mined)
    // plus 100 blocks (coinbase maturity). This test has been parameterized
    // to use the constants defined in NBitcoin, but you have to keep in mind that
    // the coinbase maturity may be defined differently in other coins.
    do! lnd.WaitForBlockHeight (BlockHeight.Zero + blocksMinedToLnd + maturityDurationInNumberOfBlocks)
    do! lnd.WaitForBalance (Money(50UL, MoneyUnit.BTC))

    // fund geewallet
    let geewalletAccountAmount = Money (25m, MoneyUnit.BTC)
    let feeRate = FeeRatePerKw 2500u
    let! _txid = lnd.SendCoins geewalletAccountAmount walletAddress feeRate

    // wait for lnd's transaction to appear in mempool
    while bitcoind.GetTxIdsInMempool().Length = 0 do
        do! Async.Sleep 500

    // We want to make sure Geewallet consideres the money received.
    // A typical number of blocks that is almost universally considered
    // 100% confirmed, is 6. Therefore we mine 7 blocks. Because we have
    // waited for the transaction to appear in bitcoind's mempool, we
    // can assume that the first of the 7 blocks will include the
    // transaction sending money to Geewallet. The next 6 blocks will
    // bury the first block, so that the block containing the transaction
    // will be 6 deep at the end of the following call to generateBlocks.
    // At that point, the 0.25 regtest coins from the above call to sendcoins
    // are considered arrived to Geewallet.
    let consideredConfirmedAmountOfBlocksPlusOne = BlockHeightOffset32 7u
    bitcoind.GenerateBlocks consideredConfirmedAmountOfBlocksPlusOne lndAddress

    Console.WriteLine(SPrintF2 "sent %A to address %A" geewalletAccountAmount walletAddress)

    do! keepSlowlyMining bitcoind lndAddress
}

[<EntryPoint>]
let main argv =
    if argv.Length <> 2 || argv.[0] <> "--wallet-address" then
        failwith "expected --wallet-address argument"
    let walletAddress = BitcoinAddress.Create(argv.[1], Network.RegTest)
    Async.RunSynchronously <| mainJob walletAddress
    0

