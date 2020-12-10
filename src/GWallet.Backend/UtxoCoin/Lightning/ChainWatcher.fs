namespace GWallet.Backend.UtxoCoin.Lightning

open System.Linq

open NBitcoin
open DotNetLightning.Channel
open DotNetLightning.Utils
open DotNetLightning.Crypto

open GWallet.Backend
open GWallet.Backend.UtxoCoin
open GWallet.Backend.FSharpUtil
open GWallet.Backend.FSharpUtil.UwpHacks


module public ChainWatcher =

    let internal CheckForChannelFraudAndSendRevocationTx (channelId: ChannelIdentifier)
                                                         (channelStore: ChannelStore)
                                                             : Async<Option<string>> = async {
        let serializedChannel = channelStore.LoadChannel channelId
        let currency = (channelStore.Account :> IAccount).Currency
        let commitments = ChannelSerialization.Commitments serializedChannel
        let fundingScriptCoin = commitments.FundingScriptCoin
        let fundingDestination: TxDestination = fundingScriptCoin.ScriptPubKey.GetDestination()
        let network = UtxoCoin.Account.GetNetwork currency
        let fundingAddress: BitcoinAddress = fundingDestination.GetAddress network
        let fundingAddressString: string = fundingAddress.ToString()

        let scriptHash =
            Account.GetElectrumScriptHashFromPublicAddress
                currency
                fundingAddressString

        let! historyList =
            Server.Query currency
                         (QuerySettings.Default ServerSelectionMode.Fast)
                         (ElectrumClient.GetBlockchainScriptHashHistory scriptHash)
                         None

        match Seq.tryItem 1 historyList with
        | None -> return None
        | Some spendingTxInfo ->
            let spendingTxId = spendingTxInfo.TxHash
            let! spendingTxString =
                Server.Query
                    currency
                    (QuerySettings.Default ServerSelectionMode.Fast)
                    (ElectrumClient.GetBlockchainTransaction spendingTxId)
                    None
            let spendingTx = Transaction.Parse(spendingTxString, network)
            
            let obscuredCommitmentNumber =
                let obscuredCommitmentNumberOpt =  
                    RemoteForceClose.tryGetObscuredCommitmentNumber
                        commitments.FundingScriptCoin.Outpoint
                        spendingTx
                UnwrapOption obscuredCommitmentNumberOpt "Tx isn't a commitmentTx"

            let localChannelPubKeys = commitments.LocalParams.ChannelPubKeys
            let remoteChannelPubKeys = commitments.RemoteParams.ChannelPubKeys

            let commitmentNumber =
                obscuredCommitmentNumber.Unobscure
                    commitments.LocalParams.IsFunder
                    localChannelPubKeys.PaymentBasepoint
                    remoteChannelPubKeys.PaymentBasepoint

            //TODO: or we could just search based on CommitmentTxHash
            let breachDataOpt = (BreachDataStore channelStore.Account)
                                   .LoadBreachData(channelId)
                                   .GetBreachData(commitmentNumber)

            match breachDataOpt with
            | None -> return None
            | Some breachData ->
                let! txId = 
                    UtxoCoin.Account.BroadcastRawTransaction currency breachData.PenaltyTx
                return Some <| txId
                
    }

    let CheckForChannelFraudsAndSendRevocationTx (accounts: seq<UtxoCoin.NormalUtxoAccount>)
                                                     : seq<Async<Option<string>>> =
        seq {
            for account in accounts do
                let channelStore = ChannelStore account
                let channelIds = channelStore.ListChannelIds()

                for channelId in channelIds do
                    yield
                        CheckForChannelFraudAndSendRevocationTx channelId channelStore
        }

