namespace GWallet.Backend.Tests.EndToEnd

open System
open System.IO // For File.WriteAllText
open System.Diagnostics // For Process
open System.Net // For IPAddress and IPEndPoint
open System.Text // For Encoding
open System.Threading // For AutoResetEvent and CancellationToken
open System.Threading.Tasks // For Task
open System.Collections.Concurrent
open Microsoft.FSharp.Linq.NullableOperators // For <>?

open Newtonsoft.Json // For JsonConvert
open BTCPayServer.Lightning
open BTCPayServer.Lightning.LND
open NBitcoin // For ExtKey
open DotNetLightning.Utils
open ResultUtils.Portability

open GWallet.Backend
open GWallet.Backend.UtxoCoin // For NormalUtxoAccount
open GWallet.Backend.UtxoCoin.Lightning
open GWallet.Backend.FSharpUtil.UwpHacks
open GWallet.Regtest

type WalletInstance private (password: string, channelStore: ChannelStore, node: Node) =
    static let oneWalletAtATime: Semaphore = new Semaphore(1, 1)

    static member New (listenEndpointOpt: Option<IPEndPoint>) (privateKeyOpt: Option<Key>) = async {
        oneWalletAtATime.WaitOne() |> ignore
        let password = Path.GetRandomFileName()
        let privateKeyBytes =
            let privateKey =
                match privateKeyOpt with
                | Some privateKey -> privateKey
                | None -> new Key()
            let privateKeyBytesLength = 32
            let bytes: array<byte> = Array.zeroCreate privateKeyBytesLength
            use bytesStream = new MemoryStream(bytes)
            let stream = NBitcoin.BitcoinStream(bytesStream, true)
            privateKey.ReadWrite stream
            bytes

        do! Account.CreateAllAccounts privateKeyBytes password
        let btcAccount =
            let account = Account.GetAllActiveAccounts() |> Seq.filter (fun x -> x.Currency = Currency.BTC) |> Seq.head
            account :?> NormalUtxoAccount
        let channelStore = ChannelStore btcAccount
        let node =
            let listenEndpoint =
                match listenEndpointOpt with
                | Some listenEndpoint -> listenEndpoint
                | None -> IPEndPoint(IPAddress.Parse "127.0.0.1", 0)
            Connection.Start channelStore password listenEndpoint
        return new WalletInstance(password, channelStore, node)
    }

    interface IDisposable with
        member this.Dispose() =
            Account.WipeAll()
            oneWalletAtATime.Release() |> ignore

    member self.Account: IAccount =
        Account.GetAllActiveAccounts() |> Seq.filter (fun x -> x.Currency = Currency.BTC) |> Seq.head

    member self.Address: BitcoinScriptAddress =
        BitcoinScriptAddress(self.Account.PublicAddress, Network.RegTest)

    member self.Password: string = password
    member self.ChannelStore: ChannelStore = channelStore
    member self.Node: Node = node
    member self.NodeEndPoint = Lightning.Network.EndPoint self.Node

    member self.GetBalance(): Async<Money> = async {
        let btcAccount = self.Account :?> NormalUtxoAccount
        let! cachedBalance = Account.GetShowableBalance btcAccount ServerSelectionMode.Analysis None
        match cachedBalance with
        | NotFresh _ ->
            do! Async.Sleep 500
            return! self.GetBalance()
        | Fresh amount -> return Money(amount, MoneyUnit.BTC)
    }

    member self.WaitForBalance(minAmount: Money): Async<Money> = async {
        let btcAccount = self.Account :?> NormalUtxoAccount
        let! cachedBalance = Account.GetShowableBalance btcAccount ServerSelectionMode.Analysis None
        match cachedBalance with
        | Fresh amount when amount < minAmount.ToDecimal MoneyUnit.BTC ->
            do! Async.Sleep 500
            return! self.WaitForBalance minAmount
        | NotFresh _ ->
            do! Async.Sleep 500
            return! self.WaitForBalance minAmount
        | Fresh amount -> return Money(amount, MoneyUnit.BTC)
    }

    member self.WaitForFundingConfirmed (channelId: ChannelIdentifier): Async<unit> =
        let channelInfo = self.ChannelStore.ChannelInfo channelId
        let fundingBroadcastButNotLockedData =
            match channelInfo.Status with
            | ChannelStatus.FundingBroadcastButNotLocked fundingBroadcastButNotLockedData
                -> fundingBroadcastButNotLockedData
            | status -> failwith (SPrintF1 "Unexpected channel status. Expected FundingBroadcastButNotLocked, got %A" status)
        let rec waitForFundingConfirmed() = async {
            let! remainingConfirmations = fundingBroadcastButNotLockedData.GetRemainingConfirmations()
            if remainingConfirmations > 0u then
                do! Async.Sleep 1000
                return! waitForFundingConfirmed()
            else
                // TODO: the backend API doesn't give us any way to avoid
                // the FundingOnChainLocationUnknown error, so just sleep
                // to avoid the race condition. This waiting should really
                // be implemented on the backend anyway.
                do! Async.Sleep 10000
                return ()
        }
        waitForFundingConfirmed()
   
    member this.FundByMining (bitcoind: Bitcoind)
                             (lnd: Lnd)
                                : Async<unit> = async {
        do! lnd.FundByMining bitcoind

        // fund geewallet
        let geewalletAccountAmount = Money (25m, MoneyUnit.BTC)
        let feeRate = FeeRatePerKw 2500u
        let! _txid = lnd.SendCoins geewalletAccountAmount this.Address feeRate

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
        bitcoind.GenerateBlocksToBurnAddress consideredConfirmedAmountOfBlocksPlusOne
    }
