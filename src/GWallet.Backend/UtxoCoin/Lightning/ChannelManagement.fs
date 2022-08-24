namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.IO

open DotNetLightning.Chain
open DotNetLightning.Channel
open DotNetLightning.Serialization.Msgs
open DotNetLightning.Utils
open ResultUtils.Portability
open NBitcoin

open GWallet.Backend
open GWallet.Backend.UtxoCoin
open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks

type FundingBroadcastButNotLockedData =
    {
        Currency: Currency
        TxId: TransactionIdentifier
        MinimumDepth: uint32
    }
    member self.GetRemainingConfirmations(): Async<uint32> =
        async {
            let! confirmationCount =
                UtxoCoin.Server.Query
                    self.Currency
                    (UtxoCoin.QuerySettings.Default ServerSelectionMode.Fast)
                    (UtxoCoin.ElectrumClient.GetConfirmations (self.TxId.ToString()))
                    None
            if confirmationCount < self.MinimumDepth then
                let remainingConfirmations = self.MinimumDepth - confirmationCount
                return remainingConfirmations
            else
                return 0u
        }

type LocallyForceClosedData =
    {
        Network: Network
        Currency: Currency
        ToSelfDelay: uint16
        ForceCloseTxId: TransactionIdentifier
        ClosingTimestampUtc: DateTime
    }
    member self.GetRemainingConfirmations (): Async<uint16> =
        async {
            let! confirmationCount =
                UtxoCoin.Server.Query
                    self.Currency
                    (UtxoCoin.QuerySettings.Default ServerSelectionMode.Fast)
                    (UtxoCoin.ElectrumClient.GetConfirmations (self.ForceCloseTxId.ToString()))
                    None
            if confirmationCount < uint32 self.ToSelfDelay then
                let remainingConfirmations = self.ToSelfDelay - uint16 confirmationCount
                return remainingConfirmations
            else
                return 0us
        }


type ChannelStatus =
    | FundingBroadcastButNotLocked of FundingBroadcastButNotLockedData
    | Closing
    | LocallyForceClosed of LocallyForceClosedData
    | RecoveryTxSentOrNotNeeded of Option<TransactionIdentifier>
    | Active

type ChannelInfo =
    {
        ChannelId: ChannelIdentifier
        IsFunder: bool
        Balance: decimal
        SpendableBalance: decimal
        Capacity: decimal
        MaxBalance: decimal
        MinBalance: decimal
        FundingOutPointIndex: uint32
        FundingTxId: TransactionIdentifier
        Status: ChannelStatus
        Currency: Currency
        NodeTransportType: NodeTransportType
    }
    static member internal FromSerializedChannel (serializedChannel: SerializedChannel)
                                                 (currency: Currency)
                                                     : ChannelInfo =
        let fundingTxId = TransactionIdentifier.FromHash (serializedChannel.FundingScriptCoin().Outpoint.Hash)
        let status =
            match serializedChannel.MainBalanceRecoveryStatus with
            | RecoveryTxSent txId ->
                txId
                |> Some
                |> ChannelStatus.RecoveryTxSentOrNotNeeded
            | NotNeeded ->
                ChannelStatus.RecoveryTxSentOrNotNeeded None
            | Unresolved ->
                match serializedChannel.ForceCloseTxIdOpt with
                | Some forceCloseTxId ->
                    ChannelStatus.LocallyForceClosed {
                        Network = serializedChannel.SavedChannelState.StaticChannelConfig.Network
                        Currency = currency
                        ToSelfDelay = serializedChannel.SavedChannelState.StaticChannelConfig.RemoteParams.ToSelfDelay.Value
                        ForceCloseTxId = forceCloseTxId
                        ClosingTimestampUtc = UnwrapOption serializedChannel.ClosingTimestampUtc "BUG: closing date is empty after local force close"
                    }
                | None ->
                    if serializedChannel.NegotiatingState.HasEnteredShutdown() then
                        ChannelStatus.Closing
                    elif serializedChannel.SavedChannelState.ShortChannelId.IsNone || serializedChannel.RemoteNextCommitInfo.IsNone then
                        ChannelStatus.FundingBroadcastButNotLocked
                            {
                                Currency = currency
                                TxId = fundingTxId
                                MinimumDepth = serializedChannel.MinDepth().Value
                            }
                    else
                        ChannelStatus.Active
        {
            ChannelId = serializedChannel.ChannelId()
            IsFunder = serializedChannel.IsFunder()
            Balance = serializedChannel.Balance().ToMoney().ToUnit MoneyUnit.BTC
            SpendableBalance = serializedChannel.SpendableBalance().ToMoney().ToUnit MoneyUnit.BTC
            Capacity = serializedChannel.Capacity().ToUnit MoneyUnit.BTC
            MaxBalance = serializedChannel.MaxBalance().ToMoney().ToUnit MoneyUnit.BTC
            MinBalance = serializedChannel.MinBalance().ToMoney().ToUnit MoneyUnit.BTC
            FundingTxId = fundingTxId
            FundingOutPointIndex = serializedChannel.FundingScriptCoin().Outpoint.N
            Currency = currency
            Status = status
            NodeTransportType = serializedChannel.NodeTransportType
        }

type ChannelStore(account: NormalUtxoAccount) =
    static member ChannelFilePrefix = "chan-"
    static member ChannelFileEnding = ".json"

    member val Account = account
    member val Currency = (account :> IAccount).Currency

    member self.AccountDir: DirectoryInfo =
        Config.GetConfigDir self.Currency AccountKind.Normal

    member self.ChannelDir: DirectoryInfo =
        Path.Combine (self.AccountDir.FullName, Settings.ConfigDirName)
        |> DirectoryInfo

    member private self.ArchivedChannelsDir: DirectoryInfo =
        Path.Combine (self.ChannelDir.FullName, "archived")
        |> DirectoryInfo

    member self.ListChannelIds(): seq<ChannelIdentifier> =
        let extractChannelId path: Option<ChannelIdentifier> =
            let fileName = Path.GetFileName path
            let withoutPrefix = fileName.Substring ChannelStore.ChannelFilePrefix.Length
            let withoutEnding =
                withoutPrefix.Substring(
                    0,
                    withoutPrefix.Length - ChannelStore.ChannelFileEnding.Length
                )
            ChannelIdentifier.Parse withoutEnding

        if self.ChannelDir.Exists then
            let files =
                Directory.GetFiles self.ChannelDir.FullName
            files |> Seq.choose extractChannelId
        else
            Seq.empty

    member self.ChannelFileName (channelId: ChannelIdentifier) (isArchived: bool): string =
        let directory =
            if isArchived then
                self.ArchivedChannelsDir.FullName
            else
                self.ChannelDir.FullName

        Path.Combine(
            directory,
            SPrintF3
                "%s%s%s"
                ChannelStore.ChannelFilePrefix
                (channelId.ToString())
                ChannelStore.ChannelFileEnding
        )

    member internal self.LoadChannel (channelId: ChannelIdentifier): SerializedChannel =
        let fileName = self.ChannelFileName channelId false
        let json = File.ReadAllText fileName
        Marshalling.DeserializeCustom<SerializedChannel> (
            json,
            SerializedChannel.LightningSerializerSettings self.Currency
        )

    member internal self.SaveChannel (serializedChannel: SerializedChannel) =
        let fileName = self.ChannelFileName (serializedChannel.ChannelId()) false
        let json =
            Marshalling.SerializeCustom (
                serializedChannel,
                SerializedChannel.LightningSerializerSettings self.Currency,
                Marshalling.DefaultFormatting
            )
        if not self.ChannelDir.Exists then
            self.ChannelDir.Create()
        File.WriteAllText(fileName, json)

    member self.ChannelInfo (channelId: ChannelIdentifier): ChannelInfo =
        let serializedChannel = self.LoadChannel channelId
        ChannelInfo.FromSerializedChannel serializedChannel self.Currency

    member self.ListChannelInfos(): seq<ChannelInfo> = seq {
        for channelId in self.ListChannelIds() do
            let channelInfo = self.ChannelInfo channelId
            if channelInfo.Status <> ChannelStatus.Closing then
                yield channelInfo
    }

    member self.ArchiveChannel (channelId: ChannelIdentifier): unit =
        let srcFileName = self.ChannelFileName channelId false
        let destFileName = self.ChannelFileName channelId true
        if not self.ArchivedChannelsDir.Exists then
            self.ArchivedChannelsDir.Create()
        File.Move (srcFileName, destFileName)

    member self.GetCommitmentTx (channelId: ChannelIdentifier): UtxoTransaction =
        let serializedChannel = self.LoadChannel channelId
        let nbitcoinTx = serializedChannel.SavedChannelState.LocalCommit.PublishableTxs.CommitTx.Value
        {
            NBitcoinTx = nbitcoinTx
        }

    member self.TryGetClosingTimestampUtc (channelId: ChannelIdentifier): Option<DateTime> =
        let serializedChannel = self.LoadChannel channelId
        serializedChannel.ClosingTimestampUtc

    member self.GetToSelfDelay (channelId: ChannelIdentifier): uint16 =
        let serializedChannel = self.LoadChannel channelId
        serializedChannel.SavedChannelState.StaticChannelConfig.LocalParams.ToSelfDelay.Value

    member self.CheckForClosingTx (channelId: ChannelIdentifier): Async<Option<ClosingTx * Option<uint32>>> =
        async {
            let serializedChannel = self.LoadChannel channelId
            let currency = self.Currency
            let network = UtxoCoin.Account.GetNetwork currency
            let fundingAddressString: string =
                let fundingAddress: BitcoinAddress =
                    let fundingDestination =
                        serializedChannel.FundingScriptCoin().ScriptPubKey.GetDestination()
                    fundingDestination.GetAddress network
                fundingAddress.ToString()
            let scriptHash = Account.GetElectrumScriptHashFromPublicAddress currency fundingAddressString
            let! historyList =
                Server.Query
                    currency
                    (QuerySettings.Default ServerSelectionMode.Fast)
                    (ElectrumClient.GetBlockchainScriptHashHistory scriptHash)
                    None

            let! currentBlockHeight = async {
                let! blockHeightResponse =
                    Server.Query currency
                        (QuerySettings.Default ServerSelectionMode.Fast)
                        (ElectrumClient.SubscribeHeaders ())
                        None
                return
                    (blockHeightResponse.Height |> uint32)
            }

            let rec findSpendingTx (historyList: List<BlockchainScriptHashHistoryInnerResult>) (transactions: List<Transaction * Option<uint32>>) = 
                async {
                    if historyList.IsEmpty then
                        return transactions
                    else
                        let txHash = historyList.Head.TxHash
                        let fundingTxHash = serializedChannel.FundingScriptCoin().Outpoint.Hash

                        let txConfirmationsOpt =
                            let reportedHeight = historyList.Head.Height
                            if reportedHeight = 0u then
                                None
                            else
                                Some (currentBlockHeight - reportedHeight)

                        let! txString =
                            Server.Query
                                currency
                                (QuerySettings.Default ServerSelectionMode.Fast)
                                (ElectrumClient.GetBlockchainTransaction txHash)
                                None

                        let tx = Transaction.Parse(txString, network)

                        let isSpendingTx =
                            Seq.exists
                                (
                                    fun (input: TxIn) ->
                                        input.PrevOut.Hash = fundingTxHash
                                )
                                tx.Inputs

                        if isSpendingTx then
                            return!
                                findSpendingTx historyList.Tail (transactions @ [(tx , txConfirmationsOpt)])
                        else
                            return!
                                findSpendingTx historyList.Tail transactions
                }

            let! spendingTxs =
                findSpendingTx
                    historyList
                    []

            let spendingTxOpt =
                List.tryExactlyOne
                    spendingTxs

            match spendingTxOpt with
            | None -> return None
            | Some (spendingTx, spendingTxConfirmationsOpt) ->

                let obscuredCommitmentNumberOpt =
                    ClosingHelpers.tryGetObscuredCommitmentNumber
                        (serializedChannel.FundingScriptCoin().Outpoint)
                        spendingTx

                match obscuredCommitmentNumberOpt with
                | Error _ ->
                    return
                        Some (
                            ClosingTx.MutualClose { MutualCloseTx.Tx = { UtxoTransaction.NBitcoinTx = spendingTx } },
                            spendingTxConfirmationsOpt
                        )
                | Ok _ ->
                    return
                        Some (
                            ClosingTx.ForceClose  { ForceCloseTx.Tx = { UtxoTransaction.NBitcoinTx = spendingTx } },
                            spendingTxConfirmationsOpt
                        )
        }

    member self.FeeUpdateRequired (channelId: ChannelIdentifier): Async<Option<decimal>> = async {
        let serializedChannel = self.LoadChannel channelId
        if not <| serializedChannel.IsFunder() then
            return None
        else
            let commitments = serializedChannel.Commitments
            let savedChannelState = serializedChannel.SavedChannelState
            let agreedUponFeeRate =
                let getFeeRateFromMsg (msg: IUpdateMsg): Option<FeeRatePerKw> =
                    match msg with
                    | :? UpdateFeeMsg as updateFeeMsg ->
                        Some updateFeeMsg.FeeRatePerKw
                    | _ -> None
                let feeRateOpt =
                    commitments.ProposedLocalChanges
                    |> List.rev
                    |> List.tryPick getFeeRateFromMsg
                match feeRateOpt with
                | Some feeRate -> feeRate
                | None ->
                    let feeRateOpt =
                        savedChannelState.LocalChanges.Signed
                        |> List.rev
                        |> List.tryPick getFeeRateFromMsg
                    match feeRateOpt with
                    | Some feeRate -> feeRate
                    | None ->
                        savedChannelState.LocalCommit.Spec.FeeRatePerKw
            let! actualFeeRate = async {
                let currency = (self.Account :> IAccount).Currency
                let! feeEstimator = FeeEstimator.Create currency
                return
                    (feeEstimator :> IFeeEstimator).GetEstSatPer1000Weight
                        ConfirmationTarget.Normal
            }
            let mismatchRatio = agreedUponFeeRate.MismatchRatio actualFeeRate
            let maxFeeRateMismatchRatio =
                MonoHopUnidirectionalChannel.DefaultMaxFeeRateMismatchRatio
            if mismatchRatio <= maxFeeRateMismatchRatio then
                return None
            else
                return Some (FeeEstimator.FeeRateToDecimal actualFeeRate)
    }

module ChannelManager =
    // difference from fee estimation in UtxoCoinAccount.fs: this is for P2WSH
    let EstimateChannelOpeningFee (account: IAccount) (amount: TransferAmount) =
        let witScriptIdLength = 32
        // this dummy address is only used for fee estimation
        let nullScriptId = NBitcoin.WitScriptId (Array.zeroCreate witScriptIdLength)
        let network = UtxoCoin.Account.GetNetwork account.Currency
        let dummyAddr = NBitcoin.BitcoinWitScriptAddress (nullScriptId, network)
        UtxoCoin.Account.EstimateTransferFeeForDestination (account :?> IUtxoAccount) amount dummyAddr

    let MarkRecoveryTxAsNotNeeded
        (channelId: ChannelIdentifier)
        (channelStore: ChannelStore)
        =
        let channelPreRecovery = channelStore.LoadChannel channelId
        let channelAfterRecovery =
            { channelPreRecovery with
                MainBalanceRecoveryStatus = NotNeeded
            }
        channelStore.SaveChannel channelAfterRecovery

    let BroadcastRecoveryTxAndCloseChannel
        (recoveryTx: RecoveryTx)
        (channelStore: ChannelStore)
        : Async<string>
        =
        async {
            let channelPreRecovery = channelStore.LoadChannel recoveryTx.ChannelId
            let! txId =
                UtxoCoin.Account.BroadcastRawTransaction
                    recoveryTx.Currency
                    (recoveryTx.Tx.ToString())
            let channelAfterRecovery =
                { channelPreRecovery with
                    MainBalanceRecoveryStatus =
                        recoveryTx.Tx.NBitcoinTx.GetHash()
                        |> TransactionIdentifier.FromHash
                        |> RecoveryTxSent
                }
            channelStore.SaveChannel channelAfterRecovery
            return txId
        }

    let BroadcastHtlcRecoveryTxAndRemoveFromWatchList
        (htlcRecoveryTx: HtlcRecoveryTx)
        (channelStore: ChannelStore)
        : Async<string>
        =
        async {
            let channelPreRecovery = channelStore.LoadChannel htlcRecoveryTx.ChannelId
            let! txId =
                UtxoCoin.Account.BroadcastRawTransaction
                    htlcRecoveryTx.Currency
                    (htlcRecoveryTx.Tx.ToString())
            let htlcToRemove =
                channelPreRecovery.HtlcDelayedTxs
                |> Seq.filter (fun (_, htlcTxId) -> htlcTxId = htlcRecoveryTx.HtlcTxId)
            let channelAfterRecovery =
                { channelPreRecovery with
                    HtlcDelayedTxs =
                        channelPreRecovery.HtlcDelayedTxs
                        |> List.except htlcToRemove
                    BroadcastedHtlcRecoveryTxs =
                        (htlcRecoveryTx.AmountInSatoshis, htlcRecoveryTx.Tx.Id)::channelPreRecovery.BroadcastedHtlcRecoveryTxs    
                }
            channelStore.SaveChannel channelAfterRecovery
            return txId
        }

    let BroadcastHtlcTxAndAddToWatchList
        (htlcTx: HtlcTx)
        (channelStore: ChannelStore)
        : Async<string>
        =
        async {
            let channelPreRecovery = channelStore.LoadChannel htlcTx.ChannelId
            let! txId =
                UtxoCoin.Account.BroadcastRawTransaction
                    htlcTx.Currency
                    (htlcTx.Tx.ToString())
            let channelAfterRecovery =
                if htlcTx.NeedsRecoveryTx then
                    { channelPreRecovery with
                        HtlcDelayedTxs =
                            (htlcTx.AmountInSatoshis, htlcTx.Tx.Id) :: channelPreRecovery.HtlcDelayedTxs
                    }
                else
                    { channelPreRecovery with
                        BroadcastedHtlcRecoveryTxs =
                            (htlcTx.AmountInSatoshis, htlcTx.Tx.Id) :: channelPreRecovery.BroadcastedHtlcRecoveryTxs
                    }

            channelStore.SaveChannel channelAfterRecovery
            return txId
        }

    type MutualCloseCpfpCreationError =
        | BalanceBelowDustLimit
        | NotEnoughFundsForFees

    let CreateCpfpTxOnMutualClose
        (channelStore: ChannelStore)
        (channelId: ChannelIdentifier)
        (closingTx: MutualCloseTx)
        (password: string)
        : Async<Result<MutualCloseCpfp, MutualCloseCpfpCreationError>> =
        async {
            let account = channelStore.Account
            //FIXME: this might crash with ReadOnly accounts, we should make sure
            //       ReadOnly is simply not supported instead of crashing
            let privateKey = Account.GetPrivateKey account password
            let serializedChannel = channelStore.LoadChannel channelId
            let currency = channelStore.Currency
            let network = UtxoCoin.Account.GetNetwork currency
            let targetAddress =
                let originAddress = (account :> IAccount).PublicAddress
                BitcoinAddress.Create(originAddress, network)
            let! feeRate = async {
                let! feeEstimator = FeeEstimator.Create currency
                return feeEstimator.FeeRatePerKw
            }

            let localShutdownScriptPubKey =
                UnwrapOption serializedChannel.NegotiatingState.LocalRequestedShutdown "BUG: local shutdown script is empty"

            let localOutputOpt =
                closingTx.Tx.NBitcoinTx.Outputs
                |> Seq.tryFind (fun output -> output.ScriptPubKey = localShutdownScriptPubKey.ScriptPubKey())

            match localOutputOpt with
            | Some localOutput ->
                let transactionBuilder = network.CreateTransactionBuilder()
                let publicKeyWitHash = (account :> IUtxoAccount).PublicKey.WitHash.ScriptPubKey
                let scriptCoin = ScriptCoin(closingTx.Tx.NBitcoinTx, localOutput, publicKeyWitHash)

                transactionBuilder.AddKeys privateKey |> ignore<TransactionBuilder>
                transactionBuilder.AddCoin scriptCoin |> ignore<TransactionBuilder>
                transactionBuilder.SendAll targetAddress |> ignore<TransactionBuilder>
                try
                    let fee =
                        FeeEstimator.EstimateCpfpFee
                            transactionBuilder
                            feeRate
                            closingTx.Tx.NBitcoinTx
                            (serializedChannel.FundingScriptCoin())
                    transactionBuilder.SendFees fee |> ignore

                    let cpfpTransaction: MutualCloseCpfp =
                        {
                            ChannelId = channelId
                            Currency = currency
                            Fee = MinerFee (fee.Satoshi, DateTime.UtcNow, currency)
                            Tx =
                                {
                                    NBitcoinTx = transactionBuilder.BuildTransaction true
                                }
                        }
                    return Ok cpfpTransaction
                with
                | :? NotEnoughFundsException ->
                    return Error NotEnoughFundsForFees
            | None ->
                return Error BalanceBelowDustLimit
        }

    let IsCpfpNeededForFundingSpendingTx
        (channelStore: ChannelStore)
        (channelId: ChannelIdentifier)
        (spendingTxId: TransactionIdentifier)
        =
        async {
            let channel = channelStore.LoadChannel channelId
            let fundingScriptCoin = channel.FundingScriptCoin()
            let currency = channelStore.Currency
            let network = UtxoCoin.Account.GetNetwork currency
            let! spendingTxString =
                Server.Query
                    currency
                    (QuerySettings.Default ServerSelectionMode.Fast)
                    (ElectrumClient.GetBlockchainTransaction (spendingTxId.ToString()))
                    None
            let spendingTxFeeRate =
                let spendingTx = Transaction.Parse(spendingTxString, network)
                spendingTx.GetFeeRate (Array.singleton (fundingScriptCoin :> ICoin))

            let! currentFeeRate = async {
                let! feeEstimator = FeeEstimator.Create currency
                return feeEstimator.FeeRatePerKw.AsNBitcoinFeeRate()
            }

            return spendingTxFeeRate.FeePerK < currentFeeRate.FeePerK
        }


    type HtlcResolveType =
        | Timeout of expiryBlockHeight: int
        | Success
        | Penalty

    type HtlcResolveDetails =
        {
            Unresolved: List<AmountInSatoshis * HtlcResolveType>
            WaitingForConfirmationToRecover: List<AmountInSatoshis * TransactionIdentifier>
            Resolved: List<AmountInSatoshis * TransactionIdentifier>
        }

    let ListAllHtlcs
        (channelStore: ChannelStore)
        (channelId: ChannelIdentifier)
        =
        let channel = channelStore.LoadChannel channelId
        let unresolvedHtlcsData =
            (HtlcsDataStore channelStore.Account)
                .LoadHtlcsData(channelId)
                .ChannelHtlcsData
        let recoveredHtlcs = channel.BroadcastedHtlcRecoveryTxs @ channel.BroadcastedHtlcTxs
        let unresolvedHtlcs =
            unresolvedHtlcsData
            |> List.map (fun htlcTransaction ->
                let amountInSatoshis = htlcTransaction.Amount.ToMoney().Satoshi |> Convert.ToUInt64
                let resolveType =
                    match htlcTransaction with
                    | ClosingHelpers.HtlcTransaction.Timeout (_, _, expiryBlockHeight, _) ->
                        HtlcResolveType.Timeout (int expiryBlockHeight)
                    | ClosingHelpers.HtlcTransaction.Penalty _ ->
                        HtlcResolveType.Penalty
                    | ClosingHelpers.HtlcTransaction.Success _ ->
                        HtlcResolveType.Success
                (amountInSatoshis, resolveType)
            )

        {
            HtlcResolveDetails.Unresolved = unresolvedHtlcs
            WaitingForConfirmationToRecover = channel.HtlcDelayedTxs
            Resolved = recoveredHtlcs
        }

    let CheckForFinishedRecoveryAndArchive
        (channelStore: ChannelStore)
        (channelId: ChannelIdentifier)
        (recoveryTxIdOpt: option<TransactionIdentifier>)
        =
        async {
            let currency = channelStore.Currency
            let! confirmationCount =
                async {
                    match recoveryTxIdOpt with
                    | Some recoveryTxId ->
                        let! confirmationCount =
                            Server.Query
                                currency
                                (QuerySettings.Default ServerSelectionMode.Fast)
                                (ElectrumClient.GetConfirmations (recoveryTxId.ToString()))
                                None
                        return BlockHeightOffset32 confirmationCount
                    | None ->
                        // Recovery tx was not needed so we fake it as confirmed
                        return Settings.DefaultTxMinimumDepth currency
                }
            if confirmationCount >= Settings.DefaultTxMinimumDepth currency then
                let noInProgressHtlcRecovery =
                    let allHtlcs = ListAllHtlcs channelStore channelId
                    allHtlcs.WaitingForConfirmationToRecover.IsEmpty && allHtlcs.Unresolved.IsEmpty
                if noInProgressHtlcRecovery then
                    channelStore.ArchiveChannel channelId
                    return true
                else
                    return false
            else
                return false
        }
