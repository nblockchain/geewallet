namespace GWallet.Backend.UtxoCoin.Lightning

open System.IO
open System
open NBitcoin

open DotNetLightning.Chain
open DotNetLightning.Channel
open DotNetLightning.Serialization.Msgs
open DotNetLightning.Utils

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

type ChannelStatus =
    | FundingBroadcastButNotLocked of FundingBroadcastButNotLockedData
    | Closing
    | Closed
    | Active
    | Broken

type ChannelInfo =
    {
        ChannelId: ChannelIdentifier
        IsFunder: bool
        Balance: decimal
        SpendableBalance: decimal
        Capacity: decimal
        MaxBalance: decimal
        MinBalance: decimal
        FundingTxId: TransactionIdentifier
        FundingOutPointIndex: uint32
        Status: ChannelStatus
        Currency: Currency
    }
    static member internal FromSerializedChannel (serializedChannel: SerializedChannel)
                                                 (currency: Currency)
                                                     : ChannelInfo = {
        ChannelId = ChannelSerialization.ChannelId serializedChannel
        IsFunder = ChannelSerialization.IsFunder serializedChannel
        Balance = (ChannelSerialization.Balance serializedChannel).ToMoney().ToUnit NBitcoin.MoneyUnit.BTC
        SpendableBalance = (ChannelSerialization.SpendableBalance serializedChannel).ToMoney().ToUnit NBitcoin.MoneyUnit.BTC
        Capacity = (ChannelSerialization.Capacity serializedChannel).ToUnit NBitcoin.MoneyUnit.BTC
        MaxBalance = (ChannelSerialization.MaxBalance serializedChannel).ToMoney().ToUnit NBitcoin.MoneyUnit.BTC
        MinBalance = (ChannelSerialization.MinBalance serializedChannel).ToMoney().ToUnit NBitcoin.MoneyUnit.BTC
        FundingTxId =
            TransactionIdentifier.FromHash (ChannelSerialization.Commitments serializedChannel).FundingScriptCoin.Outpoint.Hash
        FundingOutPointIndex = (ChannelSerialization.Commitments serializedChannel).FundingScriptCoin.Outpoint.N
        Currency = currency
        Status =
            match serializedChannel.ChanState with
            | ChannelState.Negotiating _
            | ChannelState.Closing _ ->
                Closing
            | ChannelState.Closed _ ->
                Closed
            | ChannelState.Normal _ -> ChannelStatus.Active
            | ChannelState.WaitForFundingConfirmed waitForFundingConfirmedData ->
                let txId = TransactionIdentifier.FromHash waitForFundingConfirmedData.Commitments.FundingScriptCoin.Outpoint.Hash
                let minimumDepth = serializedChannel.MinSafeDepth.Value
                let fundingBroadcastButNotLockedData = {
                    Currency = currency
                    TxId = txId
                    MinimumDepth = minimumDepth
                }
                ChannelStatus.FundingBroadcastButNotLocked fundingBroadcastButNotLockedData
            | _ -> ChannelStatus.Broken
    }

type ChannelStore(account: IUtxoAccount) =
    static member ChannelFilePrefix = "chan-"
    static member ChannelFileEnding = ".json"

    member val Account = account
    member val Currency = (account :> IAccount).Currency
    member val Network = UtxoCoin.Account.GetNetwork (account :> IAccount).Currency

    member self.AccountDir: DirectoryInfo =
        Config.GetConfigDir self.Currency AccountKind.Normal

    member self.ChannelDir: DirectoryInfo =
        let subdirectory = SPrintF2 "%s-%s" (self.Account :> IAccount).AccountFile.Name Settings.ConfigDirName
        Path.Combine (self.AccountDir.FullName, subdirectory) |> DirectoryInfo

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

    member self.ChannelFileName (channelId: ChannelIdentifier): string =
        Path.Combine(
            self.ChannelDir.FullName,
            SPrintF3
                "%s%s%s"
                ChannelStore.ChannelFilePrefix
                (channelId.ToString())
                ChannelStore.ChannelFileEnding
        )

    member self.NodeSecretFileName(): string =
        Path.Combine(
            self.ChannelDir.FullName,
            "node-secret"
        )
    
    member internal self.AddNodeSecret (nodeSecretString: string) (password: string) =
        let nodeSecret = NodeSecret <| Key.Parse(nodeSecretString, self.Network)
        let encryptedNodeSecret =
            let secret = nodeSecret.RawKey().GetBitcoinSecret self.Network
            let encryptedSecret =
                secret.PrivateKey.GetEncryptedBitcoinSecret(password, self.Network)
            encryptedSecret.ToWif()
        let path = self.NodeSecretFileName()
        if File.Exists path then
            failwith "node secret has already been added"
        File.WriteAllText(path, encryptedNodeSecret)

    member internal self.GetNodeSecretOpt (password: string): Option<string> =
        let path = self.NodeSecretFileName()
        if File.Exists path then
            let encryptedNodeSecret = File.ReadAllText path
            let encryptedSecret =
                BitcoinEncryptedSecretNoEC(encryptedNodeSecret, self.Network)
            Some <| (NodeSecret (encryptedSecret.GetKey(password))).ToString()
        else
            None

    member internal self.LoadChannel (channelId: ChannelIdentifier): SerializedChannel =
        let fileName = self.ChannelFileName channelId
        let json = File.ReadAllText fileName
        Marshalling.DeserializeCustom<SerializedChannel> (
            json,
            SerializedChannel.LightningSerializerSettings self.Currency
        )

    member internal self.SaveChannel (serializedChannel: SerializedChannel) =
        let fileName = self.ChannelFileName (ChannelSerialization.ChannelId serializedChannel)
        let json =
            Marshalling.SerializeCustom
                serializedChannel
                (SerializedChannel.LightningSerializerSettings self.Currency)
                Marshalling.DefaultFormatting
        if not self.ChannelDir.Exists then
            self.ChannelDir.Create()
        File.WriteAllText(fileName, json)

    member self.ChannelInfo (channelId: ChannelIdentifier): ChannelInfo =
        let serializedChannel = self.LoadChannel channelId
        ChannelInfo.FromSerializedChannel serializedChannel self.Currency

    member self.ListChannelInfos(): seq<ChannelInfo> = seq {
        for channelId in self.ListChannelIds() do
            let channelInfo = self.ChannelInfo channelId
            if channelInfo.Status <> ChannelStatus.Closing &&
               channelInfo.Status <> ChannelStatus.Closed then
                yield channelInfo
    }

    member self.GetCommitmentTx (channelId: ChannelIdentifier): string =
        let commitments =
            let serializedChannel = self.LoadChannel channelId
            UnwrapOption
                serializedChannel.ChanState.Commitments
                "A channel can only end up in the wallet if it has commitments."
        commitments.LocalCommit.PublishableTxs.CommitTx.Value.ToHex()

    member self.FeeUpdateRequired (channelId: ChannelIdentifier): Async<Option<decimal>> = async {
        let serializedChannel = self.LoadChannel channelId
        if not <| ChannelSerialization.IsFunder serializedChannel then
            return None
        else
            let commitments =
                UnwrapOption
                    serializedChannel.ChanState.Commitments
                    "A channel can only end up in the wallet if it has commitments."
            let agreedUponFeeRate =
                let getFeeRateFromMsg (msg: IUpdateMsg): Option<FeeRatePerKw> =
                    match msg with
                    | :? UpdateFeeMsg as updateFeeMsg ->
                        Some updateFeeMsg.FeeRatePerKw
                    | _ -> None
                let feeRateOpt =
                    commitments.LocalChanges.Proposed
                    |> List.rev
                    |> List.tryPick getFeeRateFromMsg
                match feeRateOpt with
                | Some feeRate -> feeRate
                | None ->
                    let feeRateOpt =
                        commitments.LocalChanges.Signed
                        |> List.rev
                        |> List.tryPick getFeeRateFromMsg
                    match feeRateOpt with
                    | Some feeRate -> feeRate
                    | None ->
                        commitments.LocalCommit.Spec.FeeRatePerKw
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
    let EstimateChannelOpeningFee (account: IUtxoAccount) (amount: TransferAmount) =
        let witScriptIdLength = 32
        // this dummy address is only used for fee estimation
        let nullScriptId = NBitcoin.WitScriptId (Array.zeroCreate witScriptIdLength)
        let network = UtxoCoin.Account.GetNetwork (account :> IAccount).Currency
        let dummyAddr = NBitcoin.BitcoinWitScriptAddress (nullScriptId, network)
        UtxoCoin.Account.EstimateFeeForDestination account amount dummyAddr

