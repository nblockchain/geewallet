namespace GWallet.Backend.Tests.EndToEnd

open Microsoft.FSharp.Linq.NullableOperators // For <>?

open Newtonsoft.Json // For JsonConvert

open NUnit.Framework

open GWallet.Backend

open BTCPayServer.Lightning
open BTCPayServer.Lightning.LND

open System
open System.IO // For File.WriteAllText
open System.Diagnostics // For Process
open System.Net // For IPAddress and IPEndPoint
open System.Text // For Encoding
open System.Threading // For AutoResetEvent and CancellationToken
open System.Threading.Tasks // For Task
open System.Collections.Concurrent
open System.Collections.Generic
open System.Linq

open NBitcoin // For ExtKey

open DotNetLightning.Utils
open DotNetLightning.Utils.Primitives
open ResultUtils.Portability
open GWallet.Backend.UtxoCoin // For NormalUtxoAccount
open GWallet.Backend.UtxoCoin.Lightning
open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks

type ProcessWrapper = {
    Name: string
    Process: Process
    Queue: ConcurrentQueue<string>
    Semaphore: Semaphore
    OutputFileStream: StreamWriter
} with
    interface IDisposable with
        member this.Dispose() =
            (this.OutputFileStream :> IDisposable).Dispose()
            (this.Process :> IDisposable).Dispose()

    static member New (name: string)
                      (workDir: string)
                      (arguments: string)
                      (environment: Map<string, string>)
                      (isPython: bool)
                          : ProcessWrapper =
        let timestamp() =
            // NOTE: this must be the same format used in scripts/make.fsx
            let dateTimeFormat = "yyyy-MM-dd:HH:mm:ss.ffff"
            DateTime.Now.ToString(dateTimeFormat)
        let outputFileStream =
            let outputFileName =
                let rand = new Random()
                // NOTE: the file name must end in .????????.log for the sake of scripts/make.fsx
                SPrintF3 "%s/%s.%s.log" workDir name (rand.Next().ToString("x8"))
            Infrastructure.LogDebug (SPrintF1 "Starting subprocess: %s" name)
            File.CreateText outputFileName
        outputFileStream.WriteLine(SPrintF1 "%s: <started>" (timestamp()))
        let fileName =
            let environmentPath = System.Environment.GetEnvironmentVariable "PATH"
            let pathSeparator = Path.PathSeparator
            let paths = environmentPath.Split pathSeparator
            let isWin = Path.DirectorySeparatorChar = '\\'
            let exeName =
                if isWin then
                    name + if isPython then ".py" else ".exe"
                else
                    name
            let paths = [ for x in paths do yield Path.Combine(x, exeName) ]
            let matching = paths |> List.filter File.Exists
            match matching with
            | first :: _ -> first
            | _ ->
                failwith <|
                    SPrintF3
                        "Couldn't find %s in path, tried %A, these paths matched: %A"
                        exeName
                        [ for x in paths do yield (File.Exists x, x) ]
                        matching

        let queue = ConcurrentQueue()
        let semaphore = new Semaphore(0, Int32.MaxValue)
        let startInfo =
            ProcessStartInfo (
                UseShellExecute = false,
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            )
        for kvp in environment do
            startInfo.Environment.[kvp.Key] <- kvp.Value
        let proc = new Process()
        proc.StartInfo <- startInfo
        let firstStreamEnded = ref false
        let outputHandler (_: obj) (args: DataReceivedEventArgs) =
            lock firstStreamEnded <| fun () ->
                match args.Data with
                | null ->
                    // We need to wait for both streams (stdout and stderr) to
                    // end. So output has ended and the process has exited
                    // after the second null.
                    if not !firstStreamEnded then
                        firstStreamEnded := true
                    else
                        outputFileStream.WriteLine(SPrintF1 "%s: <exited>" (timestamp()))
                        outputFileStream.Flush()
                        semaphore.Release() |> ignore
                | text ->
                    outputFileStream.WriteLine(SPrintF2 "%s: %s" (timestamp()) text)
                    outputFileStream.Flush()
                    queue.Enqueue text
                    semaphore.Release() |> ignore
        proc.OutputDataReceived.AddHandler(DataReceivedEventHandler outputHandler)
        proc.ErrorDataReceived.AddHandler(DataReceivedEventHandler outputHandler)
        proc.EnableRaisingEvents <- true
        if not(proc.Start()) then
            failwith "failed to start process"
        AppDomain.CurrentDomain.ProcessExit.AddHandler(EventHandler (fun _ _ -> proc.Close()))
        proc.BeginOutputReadLine()
        proc.BeginErrorReadLine()
        {
            Name = name
            Process = proc
            Queue = queue
            Semaphore = semaphore
            OutputFileStream = outputFileStream
        }

    member this.WaitForMessage(msgFilter: string -> bool) =
        this.Semaphore.WaitOne() |> ignore
        let running, line = this.Queue.TryDequeue()
        if running then
            if msgFilter line then
                ()
            else
                this.WaitForMessage msgFilter
        else
            failwith (this.Name + " exited without outputting message")

    member this.WaitForExit() =
        this.Semaphore.WaitOne() |> ignore
        let running, _ = this.Queue.TryDequeue()
        if running then
            this.WaitForExit()

    member this.ReadToEnd(): list<string> =
        let rec fold (lines: list<string>) =
            this.Semaphore.WaitOne() |> ignore
            let running, line = this.Queue.TryDequeue()
            if running then
                fold <| List.append lines [line]
            else
                lines
        fold List.empty

type Bitcoind = {
    WorkDir: string
    DataDir: string
    RpcUser: string
    RpcPassword: string
    ProcessWrapper: ProcessWrapper
} with
    interface IDisposable with
        member this.Dispose() =
            this.ProcessWrapper.Process.Kill()
            this.ProcessWrapper.WaitForExit()
            (this.ProcessWrapper :> IDisposable).Dispose()
            Directory.Delete(this.DataDir, true)

    static member Start(): Bitcoind =
        let workDir = TestContext.CurrentContext.WorkDirectory
        let dataDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
        Directory.CreateDirectory dataDir |> ignore
        let rpcUser = Path.GetRandomFileName()
        let rpcPassword = Path.GetRandomFileName()
        let confPath = Path.Combine(dataDir, "bitcoin.conf")
        let fakeFeeRate = !UtxoCoin.ElectrumClient.RegTestFakeFeeRate
        File.WriteAllText(
            confPath,
            SPrintF3
                "\
                txindex=1\n\
                printtoconsole=1\n\
                rpcuser=%s\n\
                rpcpassword=%s\n\
                rpcallowip=127.0.0.1\n\
                zmqpubrawblock=tcp://127.0.0.1:28332\n\
                zmqpubrawtx=tcp://127.0.0.1:28333\n\
                fallbackfee=%f\n\
                [regtest]\n\
                rpcbind=127.0.0.1\n\
                rpcport=18554"
                rpcUser
                rpcPassword
                fakeFeeRate
        )

        let processWrapper =
            ProcessWrapper.New
                "bitcoind"
                workDir
                (SPrintF1 "-regtest -datadir=%s" dataDir)
                Map.empty
                false
        processWrapper.WaitForMessage (fun msg -> msg.EndsWith "init message: Done loading")
        {
            WorkDir = workDir
            DataDir = dataDir
            RpcUser = rpcUser
            RpcPassword = rpcPassword
            ProcessWrapper = processWrapper
        }

    member this.GenerateBlocks (number: BlockHeightOffset32) (address: BitcoinAddress) =
        use bitcoinCli =
            ProcessWrapper.New
                "bitcoin-cli"
                this.WorkDir
                (SPrintF3 "-regtest -datadir=%s generatetoaddress %i %s" this.DataDir number.Value (address.ToString()))
                Map.empty
                false
        bitcoinCli.WaitForExit()

    member this.GetTxIdsInMempool(): list<TxId> =
        use bitcoinCli =
            ProcessWrapper.New
                "bitcoin-cli"
                this.WorkDir
                (SPrintF1 "-regtest -datadir=%s getrawmempool" this.DataDir)
                Map.empty
                false
        let lines = bitcoinCli.ReadToEnd()
        let output = String.concat "\n" lines
        let txIdList = JsonConvert.DeserializeObject<list<string>> output
        List.map (fun (txIdString: string) -> TxId <| uint256 txIdString) txIdList

    member this.RpcUrl: string =
        SPrintF2 "http://%s:%s@127.0.0.1:18554" this.RpcUser this.RpcPassword

type ElectrumServer = {
    DbDir: string
    ProcessWrapper: ProcessWrapper
} with
    interface IDisposable with
        member this.Dispose() =
            this.ProcessWrapper.Process.Kill()
            this.ProcessWrapper.WaitForExit()
            (this.ProcessWrapper :> IDisposable).Dispose()
            Directory.Delete(this.DbDir, true)

    static member Start(bitcoind: Bitcoind): ElectrumServer =
        let dbDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
        Directory.CreateDirectory dbDir |> ignore
        let processWrapper =
            ProcessWrapper.New
                "electrumx_server"
                bitcoind.WorkDir
                ""
                (Map.ofList <| [
                    "SERVICES", "tcp://[::1]:50001";
                    "COIN", "BitcoinSegwit";
                    "NET", "regtest";
                    "DAEMON_URL", bitcoind.RpcUrl;
                    "DB_DIRECTORY", dbDir
                ])
                true
        processWrapper.WaitForMessage (fun msg -> msg.EndsWith "TCP server listening on [::1]:50001")
        {
            DbDir = dbDir
            ProcessWrapper = processWrapper
        }

    static member EstimateFeeRate(): Async<FeeRatePerKw> = async {
        let! btcPerKB =
            let averageFee (feesFromDifferentServers: list<decimal>): decimal =
                feesFromDifferentServers.Sum() / decimal (List.length feesFromDifferentServers)
            let estimateFeeJob = ElectrumClient.EstimateFee 6
            Server.Query
                Currency.BTC
                (QuerySettings.FeeEstimation averageFee)
                estimateFeeJob
                None
        let satPerKB = (Money (btcPerKB, MoneyUnit.BTC)).ToUnit MoneyUnit.Satoshi
        // 4 weight units per byte. See segwit specs.
        let kwPerKB = 4m
        let satPerKw = satPerKB / kwPerKB
        let feeRatePerKw = FeeRatePerKw (uint32 satPerKw)
        return feeRatePerKw
    }

    static member SetEstimatedFeeRate(feeRatePerKw: FeeRatePerKw) =
        let satPerKw = decimal feeRatePerKw.Value
        let kwPerKB = 4m
        let satPerKB = satPerKw * kwPerKB
        let btcPerKB = (Money (satPerKB, MoneyUnit.Satoshi)).ToUnit MoneyUnit.BTC
        ElectrumClient.SetRegTestFakeFeeRate btcPerKB

type Lnd = {
    LndDir: string
    ProcessWrapper: ProcessWrapper
    ConnectionString: string
    ClientFactory: ILightningClientFactory
} with
    interface IDisposable with
        member this.Dispose() =
            this.ProcessWrapper.Process.Kill()
            this.ProcessWrapper.WaitForExit()
            (this.ProcessWrapper :> IDisposable).Dispose()
            Directory.Delete(this.LndDir, true)

    static member Start(bitcoind: Bitcoind): Async<Lnd> = async {
        let lndDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
        Directory.CreateDirectory lndDir |> ignore
        let processWrapper =
            let args =
                ""
                + " --bitcoin.active"
                + " --bitcoin.regtest"
                + " --bitcoin.node=bitcoind"
                + " --bitcoind.rpcuser=" + bitcoind.RpcUser
                + " --bitcoind.rpcpass=" + bitcoind.RpcPassword
                + " --bitcoind.zmqpubrawblock=tcp://127.0.0.1:28332"
                + " --bitcoind.zmqpubrawtx=tcp://127.0.0.1:28333"
                + " --bitcoind.rpchost=localhost:18554"
                + " --debuglevel=trace"
                + " --listen=127.0.0.2"
                + " --restlisten=127.0.0.2:8080"
                + " --lnddir=" + lndDir
            ProcessWrapper.New
                "lnd"
                bitcoind.WorkDir
                args
                Map.empty
                false
        processWrapper.WaitForMessage (fun msg -> msg.EndsWith "password gRPC proxy started at 127.0.0.2:8080")
        let connectionString = 
            ""
            + "type=lnd-rest;"
            + "server=https://127.0.0.2:8080;"
            + "allowinsecure=true;"
            + "macaroonfilepath=" + Path.Combine(lndDir, "data/chain/bitcoin/regtest/admin.macaroon")
        let clientFactory = new LightningClientFactory(NBitcoin.Network.RegTest) :> ILightningClientFactory
        let lndClient = clientFactory.Create connectionString :?> LndClient
        let walletPassword = Path.GetRandomFileName()
        let! genSeedResp = Async.AwaitTask <| lndClient.SwaggerClient.GenSeedAsync(null, null)
        let initWalletReq =
            LnrpcInitWalletRequest (
                Wallet_password = Encoding.ASCII.GetBytes walletPassword,
                Cipher_seed_mnemonic = genSeedResp.Cipher_seed_mnemonic
            )

        let! _ = Async.AwaitTask <| lndClient.SwaggerClient.InitWalletAsync initWalletReq
        processWrapper.WaitForMessage (fun msg -> msg.EndsWith "Server listening on 127.0.0.2:9735")
        return {
            LndDir = lndDir
            ProcessWrapper = processWrapper
            ConnectionString = connectionString
            ClientFactory = clientFactory
        }
    }

    member this.Client(): LndClient =
        this.ClientFactory.Create this.ConnectionString :?> LndClient

    member this.GetEndPoint(): Async<NodeEndPoint> = async {
        let client = this.Client()
        let! getInfo = Async.AwaitTask (client.SwaggerClient.GetInfoAsync())
        return NodeEndPoint.Parse Currency.BTC (SPrintF1 "%s@127.0.0.2:9735" getInfo.Identity_pubkey)
    }

    member this.GetDepositAddress(): Async<BitcoinAddress> =
        let client = this.Client()
        (client :> ILightningClient).GetDepositAddress ()
        |> Async.AwaitTask

    member this.GetBlockHeight(): Async<BlockHeight> = async {
        let client = this.Client()
        use task = client.SwaggerClient.GetInfoAsync()
        let! getInfo = Async.AwaitTask task
        return BlockHeight (uint32 getInfo.Block_height.Value)
    }

    member this.WaitForBlockHeight(blockHeight: BlockHeight): Async<unit> = async {
        let! currentBlockHeight = this.GetBlockHeight()
        if blockHeight > currentBlockHeight then
            this.ProcessWrapper.WaitForMessage <| fun msg ->
                msg.Contains(SPrintF1 "New block: height=%i" blockHeight.Value)
        return ()
    }

    member this.Balance(): Async<Money> = async {
        let client = this.Client()
        use task = client.SwaggerClient.WalletBalanceAsync ()
        let! balance = Async.AwaitTask task
        return Money(uint64 balance.Confirmed_balance, MoneyUnit.Satoshi)
    }

    member this.WaitForBalance(money: Money): Async<unit> = async {
        let! currentBalance = this.Balance()
        if money > currentBalance then
            this.ProcessWrapper.WaitForMessage <| fun msg ->
                msg.Contains "[walletbalance]"
            return! this.WaitForBalance money
        return ()
    }
    
    member this.SendCoins(money: Money) (address: BitcoinAddress) (feerate: FeeRatePerKw): Async<TxId> = async {
        let client = this.Client()
        let sendCoinsReq =
            LnrpcSendCoinsRequest (
                Addr = address.ToString(),
                Amount = (money.ToUnit MoneyUnit.Satoshi).ToString(),
                Sat_per_byte = feerate.Value.ToString()
            )
        use task = client.SwaggerClient.SendCoinsAsync sendCoinsReq
        let! sendCoinsResp = Async.AwaitTask task
        return TxId <| uint256 sendCoinsResp.Txid
    }

    member this.ConnectTo (nodeEndPoint: NodeEndPoint) : Async<ConnectionResult> =
        let client = this.Client()
        let nodeInfo =
            let pubKey =
                let stringified = nodeEndPoint.NodeId.ToString()
                let unstringified = PubKey stringified
                unstringified
            NodeInfo (pubKey, nodeEndPoint.IPEndPoint.Address.ToString(), nodeEndPoint.IPEndPoint.Port)
        (Async.AwaitTask: Task<ConnectionResult> -> Async<ConnectionResult>) <| (client :> ILightningClient).ConnectTo nodeInfo

    member this.OpenChannel (nodeEndPoint: NodeEndPoint)
                            (amount: Money)
                            (feeRate: FeeRatePerKw)
                                : Async<Result<unit, OpenChannelResult>> = async {
        let client = this.Client()
        let nodeInfo =
            let pubKey =
                let stringified = nodeEndPoint.NodeId.ToString()
                let unstringified = PubKey stringified
                unstringified
            NodeInfo (pubKey, nodeEndPoint.IPEndPoint.Address.ToString(), nodeEndPoint.IPEndPoint.Port)
        let openChannelReq =
            new OpenChannelRequest (
                NodeInfo = nodeInfo,
                ChannelAmount = amount,
                FeeRate = new FeeRate(Money(uint64 feeRate.Value))
            )
        let! openChannelResponse = Async.AwaitTask <| (client :> ILightningClient).OpenChannel openChannelReq
        match openChannelResponse.Result with
        | OpenChannelResult.Ok -> return Ok ()
        | err -> return Error err
    }

    member this.CloseChannel (fundingOutPoint: OutPoint)
                                 : Async<unit> = async {
        let client = this.Client()
        let fundingTxIdStr = fundingOutPoint.Hash.ToString()
        let fundingOutputIndex = fundingOutPoint.N
        try
            let! _response =
                Async.AwaitTask
                <| client.SwaggerClient.CloseChannelAsync(fundingTxIdStr, int64 fundingOutputIndex)
            return ()
        with
        | ex ->
            // BTCPayServer.Lightning is broken and doesn't handle the
            // channel-closed reply from lnd properly. This catches the exception (and
            // hopefully not other, unrelated exceptions).
            // See: https://github.com/btcpayserver/BTCPayServer.Lightning/issues/38
            match FSharpUtil.FindException<Newtonsoft.Json.JsonReaderException> ex with
            | Some ex when ex.LineNumber = 2 && ex.LinePosition = 0 -> return ()
            | _ -> return raise <| FSharpUtil.ReRaise ex
    }

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

[<TestFixture>]
type LN() =
    do Config.SetRunModeTesting()

    let FundeeAccountsPrivateKey =
        // Note: The key needs to be hard-coded, as opposed to randomly
        // generated, since it is used in two separate processes and must be
        // the same in each process.
        new Key(uint256.Parse("9d1ee30acb68716ed5f4e25b3c052c6078f1813f45d33a47e46615bfd05fa6fe").ToBytes())
    let FundeeNodePubKey =
        Connection.NodeIdAsPubKeyFromAccountPrivKey FundeeAccountsPrivateKey
    let FundeeLightningIPEndpoint = IPEndPoint (IPAddress.Parse "127.0.0.1", 9735)
    let FundeeNodeEndpoint =
        NodeEndPoint.Parse
            Currency.BTC
            (SPrintF3
                "%s@%s:%d"
                (FundeeNodePubKey.ToHex())
                (FundeeLightningIPEndpoint.Address.ToString())
                FundeeLightningIPEndpoint.Port
            )

    let WalletToWalletTestPayment0Amount = Money(0.01m, MoneyUnit.BTC)
    let WalletToWalletTestPayment1Amount = Money(0.015m, MoneyUnit.BTC)

    [<Category("GeewalletToGeewalletFunder")>]
    [<Test>]
    [<Timeout(200000)>]
    member __.``can send/receive monohop payments and close channel (funder)``() = Async.RunSynchronously <| async {
        use! walletInstance = WalletInstance.New None None
        use bitcoind = Bitcoind.Start()
        use _electrumServer = ElectrumServer.Start bitcoind
        use! lnd = Lnd.Start bitcoind

        // As explained in the other test, geewallet cannot use coinbase outputs.
        // To work around that we mine a block to a LND instance and afterwards tell
        // it to send funds to the funder geewallet instance
        let! address = lnd.GetDepositAddress()
        let blocksMinedToLnd = BlockHeightOffset32 1u
        bitcoind.GenerateBlocks blocksMinedToLnd address

        let maturityDurationInNumberOfBlocks = BlockHeightOffset32 (uint32 NBitcoin.Consensus.RegTest.CoinbaseMaturity)
        bitcoind.GenerateBlocks maturityDurationInNumberOfBlocks walletInstance.Address

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
        let! feeRate = ElectrumServer.EstimateFeeRate()
        let! _txid = lnd.SendCoins geewalletAccountAmount walletInstance.Address feeRate

        // wait for lnd's transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            Thread.Sleep 500

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
        bitcoind.GenerateBlocks consideredConfirmedAmountOfBlocksPlusOne walletInstance.Address

        let fundingAmount = Money(0.1m, MoneyUnit.BTC)
        let! transferAmount = async {
            let! accountBalance = walletInstance.WaitForBalance fundingAmount
            return TransferAmount (fundingAmount.ToDecimal MoneyUnit.BTC, accountBalance.ToDecimal MoneyUnit.BTC, Currency.BTC)
        }
        let! metadata = ChannelManager.EstimateChannelOpeningFee (walletInstance.Account :?> NormalUtxoAccount) transferAmount
        let! pendingChannelRes =
            Lightning.Network.OpenChannel
                walletInstance.Node
                FundeeNodeEndpoint
                transferAmount
        let pendingChannel = UnwrapResult pendingChannelRes "OpenChannel failed"
        let minimumDepth = (pendingChannel :> IChannelToBeOpened).ConfirmationsRequired
        let! acceptRes = pendingChannel.Accept metadata walletInstance.Password
        let (channelId, _fundingTxId) = UnwrapResult acceptRes "pendingChannel.Accept failed"
        bitcoind.GenerateBlocks (BlockHeightOffset32 minimumDepth) walletInstance.Address

        do!
            let channelInfo = walletInstance.ChannelStore.ChannelInfo channelId
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

        let! lockFundingRes = Lightning.Network.LockChannelFunding walletInstance.Node channelId
        UnwrapResult lockFundingRes "LockChannelFunding failed"

        let channelInfo = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfo.Balance, MoneyUnit.BTC) <> fundingAmount then
            failwith "balance does not match funding amount"

        let! sendMonoHopPayment0Res =
            let transferAmount =
                let accountBalance = Money(channelInfo.SpendableBalance, MoneyUnit.BTC)
                TransferAmount (WalletToWalletTestPayment0Amount.ToDecimal MoneyUnit.BTC, accountBalance.ToDecimal MoneyUnit.BTC, Currency.BTC)
            Lightning.Network.SendMonoHopPayment
                walletInstance.Node
                channelId
                transferAmount
        UnwrapResult sendMonoHopPayment0Res "SendMonoHopPayment failed"

        let channelInfoAfterPayment0 = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfoAfterPayment0.Balance, MoneyUnit.BTC) <> fundingAmount - WalletToWalletTestPayment0Amount then
            failwith "incorrect balance after payment 0"

        let! sendMonoHopPayment1Res =
            let transferAmount =
                let accountBalance = Money(channelInfo.SpendableBalance, MoneyUnit.BTC)
                TransferAmount (WalletToWalletTestPayment1Amount.ToDecimal MoneyUnit.BTC, accountBalance.ToDecimal MoneyUnit.BTC, Currency.BTC)
            Lightning.Network.SendMonoHopPayment
                walletInstance.Node
                channelId
                transferAmount
        UnwrapResult sendMonoHopPayment1Res "SendMonoHopPayment failed"

        let channelInfoAfterPayment1 = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfoAfterPayment1.Balance, MoneyUnit.BTC) <> fundingAmount - WalletToWalletTestPayment0Amount - WalletToWalletTestPayment1Amount then
            failwith "incorrect balance after payment 1"

        ElectrumServer.SetEstimatedFeeRate (feeRate * 4u)
        let! newFeeRateOpt = walletInstance.ChannelStore.FeeUpdateRequired channelId
        let newFeeRate = UnwrapOption newFeeRateOpt "Fee update should be required"
        let! updateFeeRes =
            Lightning.Network.UpdateFee walletInstance.Node channelId newFeeRate
        UnwrapResult updateFeeRes "UpdateFee failed"

        let! closeChannelRes = Lightning.Network.CloseChannel walletInstance.Node channelId
        match closeChannelRes with
        | Ok _ -> ()
        | Error err -> failwith (SPrintF1 "error when closing channel: %s" err.Message)

        match (walletInstance.ChannelStore.ChannelInfo channelId).Status with
        | ChannelStatus.Closing -> ()
        | status -> failwith (SPrintF1 "unexpected channel status. Expected Closing, got %A" status)

        // Mine 10 blocks to make sure closing tx is confirmed
        bitcoind.GenerateBlocks (BlockHeightOffset32 (uint32 10)) walletInstance.Address
        
        let rec waitForClosingTxConfirmed attempt = async {
            Infrastructure.LogDebug (SPrintF1 "Checking if closing tx is finished, attempt #%d" attempt)
            if attempt = 10 then
                return Error "Closing tx not confirmed after maximum attempts"
            else
                let! txIsConfirmed = Lightning.Network.CheckClosingFinished (walletInstance.ChannelStore.ChannelInfo channelId)
                if txIsConfirmed then
                    return Ok ()
                else
                    do! Async.Sleep 1000
                    return! waitForClosingTxConfirmed (attempt + 1)
                    
        }

        let! closingTxConfirmedRes = waitForClosingTxConfirmed 0
        match closingTxConfirmedRes with
        | Ok _ -> ()
        | Error err -> failwith (SPrintF1 "error when waiting for closing tx to confirm: %s" err)

        return ()
    }

    [<Category("GeewalletToGeewalletFundee")>]
    [<Test>]
    [<Timeout(200000)>]
    member __.``can send/receive monohop payments and close channel (fundee)``() = Async.RunSynchronously <| async {
        use! walletInstance = WalletInstance.New (Some FundeeLightningIPEndpoint) (Some FundeeAccountsPrivateKey)
        let! feeRate = ElectrumServer.EstimateFeeRate()
        let! pendingChannelRes =
            Lightning.Network.AcceptChannel
                walletInstance.Node

        let (channelId, _) = UnwrapResult pendingChannelRes "OpenChannel failed"

        let! lockFundingRes = Lightning.Network.LockChannelFunding walletInstance.Node channelId
        UnwrapResult lockFundingRes "LockChannelFunding failed"

        let channelInfo = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfo.Balance, MoneyUnit.BTC) <> Money(0.0m, MoneyUnit.BTC) then
            failwith "incorrect balance after accepting channel"

        let! receiveMonoHopPaymentRes =
            Lightning.Network.ReceiveMonoHopPayment walletInstance.Node channelId
        UnwrapResult receiveMonoHopPaymentRes "ReceiveMonoHopPayment failed"

        let channelInfoAfterPayment0 = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfoAfterPayment0.Balance, MoneyUnit.BTC) <> WalletToWalletTestPayment0Amount then
            failwith "incorrect balance after receiving payment 0"

        let! receiveMonoHopPaymentRes =
            Lightning.Network.ReceiveMonoHopPayment walletInstance.Node channelId
        UnwrapResult receiveMonoHopPaymentRes "ReceiveMonoHopPayment failed"

        let channelInfoAfterPayment1 = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfoAfterPayment1.Balance, MoneyUnit.BTC) <> WalletToWalletTestPayment0Amount + WalletToWalletTestPayment1Amount then
            failwith "incorrect balance after receiving payment 1"

        ElectrumServer.SetEstimatedFeeRate (feeRate * 4u)
        let! acceptUpdateFeeRes =
            Lightning.Network.AcceptUpdateFee walletInstance.Node channelId
        UnwrapResult acceptUpdateFeeRes "AcceptUpdateFee failed"

        let! closeChannelRes = Lightning.Network.AcceptCloseChannel walletInstance.Node channelId
        match closeChannelRes with
        | Ok _ -> ()
        | Error err -> failwith (SPrintF1 "failed to accept close channel: %A" err)

        return ()
    }

    [<Category("GeewalletToLndFunder")>]
    [<Test>]
    [<Timeout(500000)>]
    member __.``can open and close channel with LND``() = Async.RunSynchronously <| async {
        use! walletInstance = WalletInstance.New None None
        use bitcoind = Bitcoind.Start()
        use _electrumServer = ElectrumServer.Start bitcoind
        use! lnd = Lnd.Start bitcoind

        let! address = lnd.GetDepositAddress()
        let blocksMinedToLnd = BlockHeightOffset32 1u
        bitcoind.GenerateBlocks blocksMinedToLnd address

        // Geewallet cannot use these outputs, even though they are encumbered with an output
        // script from its wallet. This is because they come from coinbase. Coinbase outputs are
        // the source of all bitcoin, and as of May 2020, Geewallet does not detect coins
        // received straight from coinbase. In practice, this doesn't matter, since miners
        // do not use Geewallet. If the coins were to be detected by geewallet,
        // this test would still work. This comment is just here to avoid confusion.
        let maturityDurationInNumberOfBlocks = BlockHeightOffset32 (uint32 NBitcoin.Consensus.RegTest.CoinbaseMaturity)
        bitcoind.GenerateBlocks maturityDurationInNumberOfBlocks walletInstance.Address

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
        let! feeRate = ElectrumServer.EstimateFeeRate()
        let! _txid = lnd.SendCoins geewalletAccountAmount walletInstance.Address feeRate

        // wait for lnd's transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            Thread.Sleep 500

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
        bitcoind.GenerateBlocks consideredConfirmedAmountOfBlocksPlusOne walletInstance.Address

        let! lndEndPoint = lnd.GetEndPoint()
        let! transferAmount = async {
            let amount = Money(0.002m, MoneyUnit.BTC)
            let! accountBalance = walletInstance.WaitForBalance amount
            return TransferAmount (amount.ToDecimal MoneyUnit.BTC, accountBalance.ToDecimal MoneyUnit.BTC, Currency.BTC)
        }
        let! metadata = ChannelManager.EstimateChannelOpeningFee (walletInstance.Account :?> NormalUtxoAccount) transferAmount
        let! pendingChannelRes =
            Lightning.Network.OpenChannel
                walletInstance.Node
                lndEndPoint
                transferAmount
        let pendingChannel = UnwrapResult pendingChannelRes "OpenChannel failed"
        let minimumDepth = (pendingChannel :> IChannelToBeOpened).ConfirmationsRequired
        let! acceptRes = pendingChannel.Accept metadata walletInstance.Password
        let (channelId, _fundingTxId) = UnwrapResult acceptRes "pendingChannel.Accept failed"
        bitcoind.GenerateBlocks (BlockHeightOffset32 minimumDepth) walletInstance.Address

        do! walletInstance.WaitForFundingConfirmed channelId

        let! lockFundingRes = Lightning.Network.LockChannelFunding walletInstance.Node channelId
        UnwrapResult lockFundingRes "LockChannelFunding failed"

        let channelInfo = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        let! closeChannelRes = Lightning.Network.CloseChannel walletInstance.Node channelId
        match closeChannelRes with
        | Ok _ -> ()
        | Error err -> failwith (SPrintF1 "error when closing channel: %s" err.Message)

        match (walletInstance.ChannelStore.ChannelInfo channelId).Status with
        | ChannelStatus.Closing -> ()
        | status -> failwith (SPrintF1 "unexpected channel status. Expected Closing, got %A" status)

        // Mine 10 blocks to make sure closing tx is confirmed
        bitcoind.GenerateBlocks (BlockHeightOffset32 (uint32 10)) walletInstance.Address
        
        let rec waitForClosingTxConfirmed attempt = async {
            Infrastructure.LogDebug (SPrintF1 "Checking if closing tx is finished, attempt #%d" attempt)
            if attempt = 10 then
                return Error "Closing tx not confirmed after maximum attempts"
            else
                let! txIsConfirmed = Lightning.Network.CheckClosingFinished (walletInstance.ChannelStore.ChannelInfo channelId)
                if txIsConfirmed then
                    return Ok ()
                else
                    do! Async.Sleep 1000
                    return! waitForClosingTxConfirmed (attempt + 1)
                    
        }

        let! closingTxConfirmedRes = waitForClosingTxConfirmed 0
        match closingTxConfirmedRes with
        | Ok _ -> ()
        | Error err -> failwith (SPrintF1 "error when waiting for closing tx to confirm: %s" err)

        return ()
    }

    [<Category("GeewalletToLndFundee")>]
    [<Test>]
    [<Timeout(500000)>]
    member __.``can accept and close channel from LND``() = Async.RunSynchronously <| async {
        use! walletInstance = WalletInstance.New None None
        use bitcoind = Bitcoind.Start()
        use _electrumServer = ElectrumServer.Start bitcoind
        use! lnd = Lnd.Start bitcoind

        let! address = lnd.GetDepositAddress()
        let blocksMinedToLnd = BlockHeightOffset32 1u
        bitcoind.GenerateBlocks blocksMinedToLnd address

        // Geewallet cannot use these outputs, even though they are encumbered with an output
        // script from its wallet. This is because they come from coinbase. Coinbase outputs are
        // the source of all bitcoin, and as of May 2020, Geewallet does not detect coins
        // received straight from coinbase. In practice, this doesn't matter, since miners
        // do not use Geewallet. If the coins were to be detected by geewallet,
        // this test would still work. This comment is just here to avoid confusion.
        let maturityDurationInNumberOfBlocks = BlockHeightOffset32 (uint32 NBitcoin.Consensus.RegTest.CoinbaseMaturity)
        bitcoind.GenerateBlocks maturityDurationInNumberOfBlocks walletInstance.Address

        // We confirm the one block mined to LND, by waiting for LND to see the chain
        // at a height which has that block matured. The height at which the block will
        // be matured is 100 on regtest. Since we initialally mined one block for LND,
        // this will wait until the block height of LND reaches 1 (initial blocks mined)
        // plus 100 blocks (coinbase maturity). This test has been parameterized
        // to use the constants defined in NBitcoin, but you have to keep in mind that
        // the coinbase maturity may be defined differently in other coins.
        do! lnd.WaitForBlockHeight (BlockHeight.Zero + blocksMinedToLnd + maturityDurationInNumberOfBlocks)
        do! lnd.WaitForBalance (Money(50UL, MoneyUnit.BTC))

        let! feeRate = ElectrumServer.EstimateFeeRate()
        let acceptChannelTask = Lightning.Network.AcceptChannel walletInstance.Node
        let openChannelTask = async {
            let! connectionResult = lnd.ConnectTo walletInstance.NodeEndPoint
            match connectionResult with
            | ConnectionResult.CouldNotConnect -> failwith "could not connect"
            | _ -> ()
            return!
                lnd.OpenChannel
                    walletInstance.NodeEndPoint
                    (Money(0.002m, MoneyUnit.BTC))
                    feeRate
        }

        let! acceptChannelRes, openChannelRes = AsyncExtensions.MixedParallel2 acceptChannelTask openChannelTask
        let (channelId, _) = UnwrapResult acceptChannelRes "AcceptChannel failed"
        UnwrapResult openChannelRes "lnd.OpenChannel failed"

        // Wait for the funding transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            Thread.Sleep 500

        // Mine blocks on top of the funding transaction to make it confirmed.
        let minimumDepth = BlockHeightOffset32 6u
        bitcoind.GenerateBlocks minimumDepth walletInstance.Address

        do! walletInstance.WaitForFundingConfirmed channelId

        let! lockFundingRes = Lightning.Network.LockChannelFunding walletInstance.Node channelId
        UnwrapResult lockFundingRes "LockChannelFunding failed"

        let channelInfo = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        // Wait for lnd to realise we're offline
        do! Async.Sleep 1000
        let fundingOutPoint =
            let fundingTxId = uint256(channelInfo.FundingTxId.ToString())
            let fundingOutPointIndex = channelInfo.FundingOutPointIndex
            OutPoint(fundingTxId, fundingOutPointIndex)
        let closeChannelTask = async {
            let! connectionResult = lnd.ConnectTo walletInstance.NodeEndPoint
            match connectionResult with
            | ConnectionResult.CouldNotConnect ->
                failwith "lnd could not connect back to us"
            | _ -> ()
            do! Async.Sleep 1000
            do! lnd.CloseChannel fundingOutPoint
            return ()
        }
        let awaitCloseTask = async {
            let rec receiveEvent () = async {
                let! receivedEvent = Lightning.Network.ReceiveLightningEvent walletInstance.Node channelId
                match receivedEvent with
                | Error err ->
                    return Error (SPrintF1 "Failed to receive shutdown msg from LND: %A" err)
                | Ok event when event = IncomingChannelEvent.Shutdown ->
                    return Ok ()
                | _ -> return! receiveEvent ()
            }

            let! receiveEventRes = receiveEvent()
            UnwrapResult receiveEventRes "failed to accept close channel"

            // Wait for the closing transaction to appear in mempool
            while bitcoind.GetTxIdsInMempool().Length = 0 do
                Thread.Sleep 500

            // Mine blocks on top of the closing transaction to make it confirmed.
            let minimumDepth = BlockHeightOffset32 6u
            bitcoind.GenerateBlocks minimumDepth walletInstance.Address
            return ()
        }

        let! (), () = AsyncExtensions.MixedParallel2 closeChannelTask awaitCloseTask

        return ()
    }

    [<Category("RevocationFunder")>]
    [<Test>]
    [<Timeout(200000)>]
    member __.``can revoke commitment tx (funder)``() = Async.RunSynchronously <| async {
        use! walletInstance = WalletInstance.New None None
        use bitcoind = Bitcoind.Start()
        use _electrumServer = ElectrumServer.Start bitcoind
        use! lnd = Lnd.Start bitcoind

        // As explained in the other test, geewallet cannot use coinbase outputs.
        // To work around that we mine a block to a LND instance and afterwards tell
        // it to send funds to the funder geewallet instance
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
        let! feeRate = ElectrumServer.EstimateFeeRate()
        let! _txid = lnd.SendCoins geewalletAccountAmount walletInstance.Address feeRate

        // wait for lnd's transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            Thread.Sleep 500

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

        let fundingAmount = Money(0.1m, MoneyUnit.BTC)
        let! transferAmount = async {
            let! accountBalance = walletInstance.WaitForBalance fundingAmount
            return TransferAmount (fundingAmount.ToDecimal MoneyUnit.BTC, accountBalance.ToDecimal MoneyUnit.BTC, Currency.BTC)
        }
        let! metadata = ChannelManager.EstimateChannelOpeningFee (walletInstance.Account :?> NormalUtxoAccount) transferAmount
        let! pendingChannelRes =
            Lightning.Network.OpenChannel
                walletInstance.Node
                FundeeNodeEndpoint
                transferAmount
        let pendingChannel = UnwrapResult pendingChannelRes "OpenChannel failed"
        let minimumDepth = (pendingChannel :> IChannelToBeOpened).ConfirmationsRequired
        let! acceptRes = pendingChannel.Accept metadata walletInstance.Password
        let (channelId, _fundingTxId) = UnwrapResult acceptRes "pendingChannel.Accept failed"
        bitcoind.GenerateBlocks (BlockHeightOffset32 minimumDepth) lndAddress

        do!
            let channelInfo = walletInstance.ChannelStore.ChannelInfo channelId
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

        let! lockFundingRes = Lightning.Network.LockChannelFunding walletInstance.Node channelId
        UnwrapResult lockFundingRes "LockChannelFunding failed"

        let channelInfo = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfo.Balance, MoneyUnit.BTC) <> fundingAmount then
            failwith "balance does not match funding amount"

        let! sendMonoHopPayment0Res =
            let transferAmount =
                let accountBalance = Money(channelInfo.SpendableBalance, MoneyUnit.BTC)
                TransferAmount (WalletToWalletTestPayment0Amount.ToDecimal MoneyUnit.BTC, accountBalance.ToDecimal MoneyUnit.BTC, Currency.BTC)
            Lightning.Network.SendMonoHopPayment
                walletInstance.Node
                channelId
                transferAmount
        UnwrapResult sendMonoHopPayment0Res "SendMonoHopPayment failed"

        let channelInfoAfterPayment0 = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfoAfterPayment0.Balance, MoneyUnit.BTC) <> fundingAmount - WalletToWalletTestPayment0Amount then
            failwith "incorrect balance after payment 0"

        let commitmentTx = walletInstance.ChannelStore.GetCommitmentTx channelId

        let! sendMonoHopPayment1Res =
            let transferAmount =
                let accountBalance = Money(channelInfo.SpendableBalance, MoneyUnit.BTC)
                TransferAmount (WalletToWalletTestPayment1Amount.ToDecimal MoneyUnit.BTC, accountBalance.ToDecimal MoneyUnit.BTC, Currency.BTC)
            Lightning.Network.SendMonoHopPayment
                walletInstance.Node
                channelId
                transferAmount
        UnwrapResult sendMonoHopPayment1Res "SendMonoHopPayment failed"

        let channelInfoAfterPayment1 = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfoAfterPayment1.Balance, MoneyUnit.BTC) <> fundingAmount - WalletToWalletTestPayment0Amount - WalletToWalletTestPayment1Amount then
            failwith "incorrect balance after payment 1"

        let! _theftTxId = UtxoCoin.Account.BroadcastRawTransaction Currency.BTC commitmentTx

        // wait for theft transaction to appear in mempool
        while bitcoind.GetTxIdsInMempool().Length = 0 do
            do! Async.Sleep 500
        
        // mine the theft tx into a block
        bitcoind.GenerateBlocks (BlockHeightOffset32 1u) lndAddress

        let! accountBalanceBeforeSpendingTheftTx =
            walletInstance.GetBalance()

        // attempt to broadcast tx which spends the theft tx
        let rec checkForClosingTx() = async {
            let! txStringOpt = Lightning.Network.CheckForClosingTx walletInstance.Node channelId
            match txStringOpt with
            | None ->
                do! Async.Sleep 500
                return! checkForClosingTx()
            | Some txString ->
                try
                    let! _txIdString = UtxoCoin.Account.BroadcastRawTransaction Currency.BTC txString
                    ()
                with
                | ex ->
                    // electrum is allowed to reject the tx because it conflicts with the penalty tx broadcast by the fundee
                    if (FSharpUtil.FindException<UtxoCoin.ElectrumServerReturningErrorException> ex).IsNone then
                        raise <| FSharpUtil.ReRaise ex
                return ()
        }
        do! checkForClosingTx()

        // give the fundee plenty of time to broadcast the penalty tx
        do! Async.Sleep 10000

        // mine enough blocks to confirm whichever tx spends the theft tx
        bitcoind.GenerateBlocks (BlockHeightOffset32 minimumDepth) lndAddress

        let! accountBalanceAfterSpendingTheftTx =
            walletInstance.GetBalance()

        if accountBalanceBeforeSpendingTheftTx <> accountBalanceAfterSpendingTheftTx then
            failwithf
                "Unexpected account balance! before theft tx == %A, after theft tx == %A"
                accountBalanceBeforeSpendingTheftTx
                accountBalanceAfterSpendingTheftTx

        // give the fundee plenty of time to see that their tx was mined
        do! Async.Sleep 5000

        return ()
    }

    [<Category("RevocationFundee")>]
    [<Test>]
    [<Timeout(200000)>]
    member __.``can revoke commitment tx (fundee)``() = Async.RunSynchronously <| async {
        use! walletInstance = WalletInstance.New (Some FundeeLightningIPEndpoint) (Some FundeeAccountsPrivateKey)
        let! pendingChannelRes =
            Lightning.Network.AcceptChannel
                walletInstance.Node

        let (channelId, _) = UnwrapResult pendingChannelRes "OpenChannel failed"

        let! lockFundingRes = Lightning.Network.LockChannelFunding walletInstance.Node channelId
        UnwrapResult lockFundingRes "LockChannelFunding failed"

        let channelInfo = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfo.Balance, MoneyUnit.BTC) <> Money(0.0m, MoneyUnit.BTC) then
            failwith "incorrect balance after accepting channel"

        let! receiveMonoHopPaymentRes =
            Lightning.Network.ReceiveMonoHopPayment walletInstance.Node channelId
        UnwrapResult receiveMonoHopPaymentRes "ReceiveMonoHopPayment failed"

        let channelInfoAfterPayment0 = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfoAfterPayment0.Balance, MoneyUnit.BTC) <> WalletToWalletTestPayment0Amount then
            failwith "incorrect balance after receiving payment 0"

        let! receiveMonoHopPaymentRes =
            Lightning.Network.ReceiveMonoHopPayment walletInstance.Node channelId
        UnwrapResult receiveMonoHopPaymentRes "ReceiveMonoHopPayment failed"

        let channelInfoAfterPayment1 = walletInstance.ChannelStore.ChannelInfo channelId
        match channelInfo.Status with
        | ChannelStatus.Active -> ()
        | status -> failwith (SPrintF1 "unexpected channel status. Expected Active, got %A" status)

        if Money(channelInfoAfterPayment1.Balance, MoneyUnit.BTC) <> WalletToWalletTestPayment0Amount + WalletToWalletTestPayment1Amount then
            failwith "incorrect balance after receiving payment 1"

        let rec checkForClosingTx() = async {
            let! txIdOpt = Lightning.Network.CheckForChannelFraudAndSendRevocationTx walletInstance.Node channelId
            match txIdOpt with
            | None ->
                do! Async.Sleep 500
                return! checkForClosingTx()
            | Some _ ->
                return ()
        }
        do! checkForClosingTx()

        let! _accountBalance =
            // wait for any amount of money to appear in the wallet
            let amount = Money(1.0m, MoneyUnit.Satoshi)
            walletInstance.WaitForBalance amount

        return ()
    }

