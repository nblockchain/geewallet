open System
open System.IO
open System.Linq
open System.Text.RegularExpressions
open System.Net

open ResultUtils.Portability

open GWallet.Backend
open GWallet.Backend.FSharpUtil
open GWallet.Backend.UtxoCoin
open GWallet.Backend.UtxoCoin.Lightning
open GWallet.Frontend.Console

let CreateFundingTx (account: UtxoCoin.IUtxoAccount)
                    (metadata: TransactionMetadata)
                    (pendingChannel: PendingChannel)
                    (password: string)
                        : string =
    match account with
    | :? UtxoCoin.NormalUtxoAccount as normalAccount ->
        UtxoCoin.Account.SignTransactionForDestination
            normalAccount
            metadata
            pendingChannel.FundingDestination
            pendingChannel.TransferAmount
            password
    | :? UtxoCoin.ReadOnlyUtxoAccount ->
        Console.WriteLine("To open a channel from a read-only account the paired wallet must \
                           sign the funding transaction")
        Console.Write("Introduce a file name to save the unsigned transaction: ")
        let unsignedFilePath = Console.ReadLine()
        let proposal = {
            OriginAddress = account.PublicAddress
            Amount = pendingChannel.TransferAmount
            DestinationAddress = pendingChannel.FundingDestinationString
        }
        Account.SaveUnsignedTransaction proposal metadata unsignedFilePath
        Console.WriteLine("Transaction saved. Now copy it to the device with the private \
                           key to sign it.")
        let rec getSignedTransaction() =
            let signedFilePath =
                UserInteraction.AskFileNameToLoad("Enter file name to load the signed transaction: ")
            let signedTransaction = Account.LoadSignedTransactionFromFile signedFilePath.FullName
            if signedTransaction.TransactionInfo.Proposal.DestinationAddress <> proposal.DestinationAddress then
                Console.WriteLine("Transaction data does not match. Incorrect file?")
                getSignedTransaction()
            else 
                signedTransaction.RawTransaction
        getSignedTransaction()
    | _ -> failwith "invalid account type"

let OpenChannel(): Async<unit> = async {
    let account = UserInteraction.AskBitcoinAccount()
    let currency = (account :> IAccount).Currency
    let channelStore = ChannelStore account

    match UserInteraction.AskAmount account with
    | None -> return ()
    | Some channelCapacity ->
        match UserInteraction.AskChannelCounterpartyConnectionDetails currency with
        | None -> return ()
        | Some nodeEndPoint ->
            Infrastructure.LogDebug "Calling EstimateFee..."
            let! metadataOpt = async {
                try
                    let! metadata = UtxoCoin.Lightning.ChannelManager.EstimateChannelOpeningFee account channelCapacity
                    return Some metadata
                with
                | InsufficientBalanceForFee _ ->
                    Console.WriteLine
                        "Estimated fee is too high for the remaining balance, \
                        use a different account or a different amount."
                    return None
            }
            match metadataOpt with
            | None -> return ()
            | Some metadata ->
                Presentation.ShowFeeAndSpendableBalance metadata channelCapacity

                let acceptFeeRate = UserInteraction.AskYesNo "Do you accept?"
                if acceptFeeRate then
                    let password = UserInteraction.AskPassword false
                    let bindAddress = IPEndPoint(IPAddress.Parse "127.0.0.1", 0)
                    use lightningNode = Lightning.Connection.Start channelStore password bindAddress
                    let! pendingChannelRes =
                        Lightning.Network.OpenChannel
                            lightningNode
                            nodeEndPoint
                            channelCapacity
                    match pendingChannelRes with
                    | Error nodeOpenChannelError ->
                        Console.WriteLine (sprintf "Error opening channel: %s" nodeOpenChannelError.Message)
                    | Ok pendingChannel ->
                        let minimumDepth = (pendingChannel :> IChannelToBeOpened).ConfirmationsRequired
                        Console.WriteLine(
                            sprintf
                                "Opening a channel with this party will require %i confirmations (~%i minutes)"
                                minimumDepth
                                (minimumDepth * 10u)
                        )
                        let acceptMinimumDepth = UserInteraction.AskYesNo "Do you accept?"
                        if acceptMinimumDepth then
                            let transactionHex =
                                CreateFundingTx
                                    account
                                    metadata
                                    pendingChannel
                                    password
                            let! acceptRes = pendingChannel.Accept transactionHex
                            match acceptRes with
                            | Error fundChannelError ->
                                Console.WriteLine(sprintf "Error funding channel: %s" fundChannelError.Message)
                            | Ok (_channelId, txId) ->
                                let uri = BlockExplorer.GetTransaction currency (TxId.ToString txId)
                                Console.WriteLine(sprintf "A funding transaction was broadcast: %A" uri)
            UserInteraction.PressAnyKeyToContinue()
}

let AcceptChannel(): Async<unit> = async {
    let account = UserInteraction.AskBitcoinAccount()
    let channelStore = ChannelStore account
    let bindAddress = UserInteraction.AskBindAddress()
    let password = UserInteraction.AskPassword false
    use lightningNode = Lightning.Connection.Start channelStore password bindAddress
    let nodeEndPoint = Lightning.Network.EndPoint lightningNode
    Console.WriteLine(sprintf "This node, connect to it: %s" (nodeEndPoint.ToString()))
    let! acceptChannelRes = Lightning.Network.AcceptChannel lightningNode
    match acceptChannelRes with
    | Error nodeAcceptChannelError ->
        Console.WriteLine
            (sprintf "Error accepting channel: %s" nodeAcceptChannelError.Message)
    | Ok (_, txId) ->
        Console.WriteLine (sprintf "Channel opened. Transaction ID: %s" (TxId.ToString txId))
        Console.WriteLine "Waiting for funding locked."
    UserInteraction.PressAnyKeyToContinue()
}

let SendLightningPayment(): Async<unit> = async {
    let account = UserInteraction.AskBitcoinAccount()
    let channelStore = ChannelStore account
    let channelIdOpt = UserInteraction.AskChannelId channelStore (Some true)
    match channelIdOpt with
    | None -> return ()
    | Some channelId ->
        let channelInfo = channelStore.ChannelInfo channelId
        let transferAmountOpt = UserInteraction.AskLightningAmount channelInfo
        match transferAmountOpt with
        | None -> ()
        | Some transferAmount ->
            let password = UserInteraction.AskPassword false
            let bindAddress = IPEndPoint(IPAddress.Parse "127.0.0.1", 0)
            use lightningNode = Lightning.Connection.Start channelStore password bindAddress
            let! paymentRes = Lightning.Network.SendMonoHopPayment lightningNode channelId transferAmount
            match paymentRes with
            | Error nodeSendMonoHopPaymentError ->
                Console.WriteLine(sprintf "Error sending monohop payment: %s" nodeSendMonoHopPaymentError.Message)
            | Ok () ->
                Console.WriteLine "Payment sent."
            UserInteraction.PressAnyKeyToContinue()
}

let CloseChannel(): Async<unit> =
    async {
        let account = UserInteraction.AskBitcoinAccount()
        let channelStore = ChannelStore account
        let channelIdOpt = UserInteraction.AskChannelId channelStore None
        match channelIdOpt with
        | None -> return ()
        | Some channelId ->
            let password = UserInteraction.AskPassword false
            let bindAddress = IPEndPoint(IPAddress.Parse "127.0.0.1", 0)
            use lightningNode = Lightning.Connection.Start channelStore password bindAddress
            let! closeRes = Lightning.Network.CloseChannel lightningNode channelId
            match closeRes with
            | Error closeError ->
                return failwithf "Error closing channel: %s" closeError.Message
            | Ok () ->
                Console.WriteLine "Channel closed."
            UserInteraction.PressAnyKeyToContinue()
            return ()
    }

let AcceptLightningEvent(): Async<unit> = async {
    let account = UserInteraction.AskBitcoinAccount()
    let channelStore = ChannelStore account
    let channelIdOpt = UserInteraction.AskChannelId channelStore (Some false)
    match channelIdOpt with
    | None -> return ()
    | Some channelId ->
        let bindAddress = UserInteraction.AskBindAddress()
        let password = UserInteraction.AskPassword false
        use lightningNode = Lightning.Connection.Start channelStore password bindAddress

        let! receiveLightningEventRes = Lightning.Network.ReceiveLightningEvent lightningNode channelId
        match receiveLightningEventRes with
        | Error nodeReceiveLightningEventError ->
            Console.WriteLine(sprintf "Error receiving lightning event: %s" nodeReceiveLightningEventError.Message)
        | Ok msg ->
            match msg with
            | IncomingChannelEvent.MonoHopUnidirectionalPayment ->
                Console.WriteLine "Payment received."
            | IncomingChannelEvent.Shutdown ->
                Console.WriteLine "Channel closed."
        UserInteraction.PressAnyKeyToContinue()
}

let LockChannel (channelStore: ChannelStore)
                (channelInfo: ChannelInfo)
                    : Async<seq<string>> = async {
    let channelId = channelInfo.ChannelId
    Console.WriteLine(sprintf "Funding for channel %s confirmed" (ChannelId.ToString channelId))
    Console.WriteLine "In order to continue the funding for the channel needs to be locked"
    let bindAddress =
        if channelInfo.IsFunder then
            Console.WriteLine
                "Ensure the fundee is ready to accept a connection to lock the funding, \
                then press any key to continue."
            Console.ReadKey true |> ignore
            IPEndPoint(IPAddress.Parse "127.0.0.1", 0)
        else
            Console.WriteLine "Listening for connection from peer"
            UserInteraction.AskBindAddress()
    let password = UserInteraction.AskPassword false
    use lightningNode = Lightning.Connection.Start channelStore password bindAddress
    let! maybeUsdValue = FiatValueEstimation.UsdValue (channelStore.Account :> IAccount).Currency
    let! lockFundingRes = Lightning.Network.LockChannelFunding lightningNode channelId
    match lockFundingRes with
    | Error lockFundingError ->
        Console.WriteLine(sprintf "Error reestablishing channel: %s" lockFundingError.Message)
    | Ok () ->
        Console.WriteLine(sprintf "funding locked for channel %s" (ChannelId.ToString channelId))
    return seq {
        yield! UserInteraction.DisplayLightningChannelStatus channelInfo maybeUsdValue
        yield "        funding locked - channel is now active"
    }
}

let LockChannelIfFundingConfirmed (channelStore: ChannelStore)
                                  (channelInfo: ChannelInfo)
                                  (fundingBroadcastButNotLockedData: FundingBroadcastButNotLockedData)
                                      : Async<Async<seq<string>>> = async {
    let! maybeUsdValue = FiatValueEstimation.UsdValue (channelStore.Account :> IAccount).Currency
    let! remainingConfirmations = fundingBroadcastButNotLockedData.GetRemainingConfirmations()
    if remainingConfirmations = 0u then
        return LockChannel channelStore channelInfo
    else
        return async {
            return seq {
                yield! UserInteraction.DisplayLightningChannelStatus channelInfo maybeUsdValue
                yield sprintf "        waiting for %i more confirmations" remainingConfirmations
            }
        }
}

let UpdateFeeIfNecessary (channelStore: ChannelStore)
                         (channelInfo: ChannelInfo)
                             : Async<Async<seq<string>>> = async {
    let channelId = channelInfo.ChannelId
    let! feeUpdateRequired, maybeUsdValue =
        AsyncExtensions.MixedParallel2
            (channelStore.FeeUpdateRequired channelId)
            (FiatValueEstimation.UsdValue (channelStore.Account :> IAccount).Currency)
    return async {
        match feeUpdateRequired with
        | None -> ()
        | Some feeRate ->
            Console.WriteLine(sprintf "Fee update required for channel %s" (ChannelId.ToString channelId))
            let bindAddress =
                Console.WriteLine
                    "Ensure the fundee is ready to accept a connection to update the fee, \
                    then press any key to continue."
                Console.ReadKey true |> ignore
                IPEndPoint(IPAddress.Parse "127.0.0.1", 0)
            let password = UserInteraction.AskPassword false
            use lightningNode = Lightning.Connection.Start channelStore password bindAddress
            let! updateFeeRes = Lightning.Network.UpdateFee lightningNode channelId feeRate
            match updateFeeRes with
            | Error updateFeeError ->
                Console.WriteLine(sprintf "Error updating fee: %s" updateFeeError.Message)
            | Ok () ->
                Console.WriteLine(sprintf "fee updated for channel %s" (ChannelId.ToString channelId))
        return seq {
            yield! UserInteraction.DisplayLightningChannelStatus channelInfo maybeUsdValue
            yield "        channel is active"
        }
    }
}

let GetChannelStatuses (accounts: seq<IAccount>): seq<Async<Async<seq<string>>>> = seq {
    let normalUtxoAccounts = accounts.OfType<UtxoCoin.IUtxoAccount>()
    for account in normalUtxoAccounts do
        let maybeUsdValue =
            FiatValueEstimation.UsdValue (account :> IAccount).Currency
            |> Async.RunSynchronously
        let channelStore = ChannelStore account
        let channelIds = channelStore.ListChannelIds()
        yield async {
            return async {
                return seq {
                    yield sprintf "Lightning Status (%i channels)" (Seq.length channelIds)
                }
            }
        }
        for channelId in channelIds do
                let channelInfo = channelStore.ChannelInfo channelId
                match channelInfo.Status with
                | ChannelStatus.FundingBroadcastButNotLocked fundingBroadcastButNotLockedData ->
                    yield LockChannelIfFundingConfirmed channelStore channelInfo fundingBroadcastButNotLockedData
                | ChannelStatus.Active ->
                    yield async {
                        return async {
                            return seq {
                                yield! UserInteraction.DisplayLightningChannelStatus channelInfo maybeUsdValue
                                yield "        channel is active"
                            }
                        }
                    }
                | ChannelStatus.Broken ->
                    yield async {
                        return async {
                            return seq {
                                yield! UserInteraction.DisplayLightningChannelStatus channelInfo maybeUsdValue
                                yield "        channel is in an abnormal state"
                            }
                        }
                    }
                | ChannelStatus.Closing ->
                    yield async {
                        return async {
                            let! isClosed = UtxoCoin.Lightning.Network.CheckClosingFinished channelInfo
                            let showTheChannel = not isClosed
                            if showTheChannel then
                                return seq {
                                    yield! UserInteraction.DisplayLightningChannelStatus channelInfo maybeUsdValue
                                    yield "        channel is in being closed"
                                }
                            else
                                return Seq.empty
                        }
                    }
                | ChannelStatus.Closed ->
                    ()

}

let rec TrySendAmount (account: NormalAccount) transactionMetadata destination amount =
    let password = UserInteraction.AskPassword false
    try
        let txIdUri =
            Account.SendPayment account transactionMetadata destination amount password
                |> Async.RunSynchronously
        Console.WriteLine(sprintf "Transaction successful:%s%s" Environment.NewLine (txIdUri.ToString()))
        UserInteraction.PressAnyKeyToContinue ()
    with
    | :? DestinationEqualToOrigin ->
        Presentation.Error "Transaction's origin cannot be the same as the destination."
        UserInteraction.PressAnyKeyToContinue()
    | :? InsufficientFunds ->
        Presentation.Error "Insufficient funds."
        UserInteraction.PressAnyKeyToContinue()
    | :? InvalidPassword ->
        Presentation.Error "Invalid password, try again."
        TrySendAmount account transactionMetadata destination amount

let rec TrySign account unsignedTrans =
    let password = UserInteraction.AskPassword false
    try
        Account.SignUnsignedTransaction account unsignedTrans password
    with
    // TODO: would this throw insufficient funds? test
    //| :? InsufficientFunds ->
    //    Presentation.Error "Insufficient funds."
    | :? InvalidPassword ->
        Presentation.Error "Invalid password, try again."
        TrySign account unsignedTrans

let BroadcastPayment() =
    let fileToReadFrom = UserInteraction.AskFileNameToLoad
                             "Introduce a file name to load the signed transaction: "
    let signedTransaction = Account.LoadSignedTransactionFromFile fileToReadFrom.FullName
    //TODO: check if nonce matches, if not, reject trans

    // FIXME: we should be able to infer the trans info from the raw transaction! this way would be more secure too
    Presentation.ShowTransactionData(signedTransaction.TransactionInfo)
    if UserInteraction.AskYesNo "Do you accept?" then
        try
            let txIdUri =
                Account.BroadcastTransaction signedTransaction
                    |> Async.RunSynchronously
            Console.WriteLine(sprintf "Transaction successful:%s%s" Environment.NewLine (txIdUri.ToString()))
            UserInteraction.PressAnyKeyToContinue ()
        with
        | :? DestinationEqualToOrigin ->
            Presentation.Error "Transaction's origin cannot be the same as the destination."
            UserInteraction.PressAnyKeyToContinue()
        | :? InsufficientFunds ->
            Presentation.Error "Insufficient funds."
            UserInteraction.PressAnyKeyToContinue()

let SignOffPayment() =
    let fileToReadFrom = UserInteraction.AskFileNameToLoad
                             "Introduce a file name to load the unsigned transaction: "
    let unsignedTransaction = Account.LoadUnsignedTransactionFromFile fileToReadFrom.FullName

    let accountsWithSameAddress =
        Account.GetAllActiveAccounts().Where(fun acc -> acc.PublicAddress = unsignedTransaction.Proposal.OriginAddress)
    if not (accountsWithSameAddress.Any()) then
        Presentation.Error "The transaction doesn't correspond to any of the accounts in the wallet."
        UserInteraction.PressAnyKeyToContinue ()
    else
        let accounts =
            accountsWithSameAddress.Where(
                fun acc -> acc.Currency = unsignedTransaction.Proposal.Amount.Currency &&
                           acc :? NormalAccount)
        if not (accounts.Any()) then
            Presentation.Error(
                sprintf
                    "The transaction corresponds to an address of the accounts in this wallet, but it's a readonly account or it maps a different currency than %A."
                    unsignedTransaction.Proposal.Amount.Currency
            )
            UserInteraction.PressAnyKeyToContinue()
        else
            let account = accounts.First()
            if (accounts.Count() > 1) then
                failwith "More than one normal account matching address and currency? Please report this issue."

            match account with
            | :? ReadOnlyAccount ->
                failwith "Previous account filtering should have discarded readonly accounts already. Please report this issue"
            | :? NormalAccount as normalAccount ->
                Console.WriteLine ("Importing external data...")
                Caching.Instance.SaveSnapshot unsignedTransaction.Cache

                Console.WriteLine ("Account to use when signing off this transaction:")
                Console.WriteLine ()
                let linesJob = UserInteraction.DisplayAccountStatuses <| WhichAccount.MatchingWith account
                for line in Async.RunSynchronously linesJob do
                    Console.WriteLine line
                Console.WriteLine()

                Presentation.ShowTransactionData unsignedTransaction

                if UserInteraction.AskYesNo "Do you accept?" then
                    let trans = TrySign normalAccount unsignedTransaction
                    Console.WriteLine("Transaction signed.")
                    Console.Write("Introduce a file name or path to save it: ")
                    let filePathToSaveTo = Console.ReadLine()
                    Account.SaveSignedTransaction trans filePathToSaveTo
                    Console.WriteLine("Transaction signed and saved successfully. Now copy it to the online device.")
                    UserInteraction.PressAnyKeyToContinue ()
            | _ ->
                failwith "Account type not supported. Please report this issue."

let SendPaymentOfSpecificAmount (account: IAccount)
                                (amount: TransferAmount)
                                (transactionMetadata: IBlockchainFeeInfo)
                                (destination: string) =
    match account with
    | :? ReadOnlyAccount ->
        Console.WriteLine("Cannot send payments from readonly accounts.")
        Console.Write("Introduce a file name to save the unsigned transaction: ")
        let filePath = Console.ReadLine()
        let proposal = {
            OriginAddress = account.PublicAddress;
            Amount = amount;
            DestinationAddress = destination;
        }
        Account.SaveUnsignedTransaction proposal transactionMetadata filePath
        Console.WriteLine("Transaction saved. Now copy it to the device with the private key.")
        UserInteraction.PressAnyKeyToContinue()
    | :? NormalAccount as normalAccount ->
        TrySendAmount normalAccount transactionMetadata destination amount
    | _ ->
        failwith ("Account type not recognized: " + account.GetType().FullName)

let SendPayment() =
    let account = UserInteraction.AskAccount()
    let destination = UserInteraction.AskPublicAddress account.Currency "Destination address: "
    let maybeAmount = UserInteraction.AskAmount account
    match maybeAmount with
    | None -> ()
    | Some(amount) ->
        let maybeFee = UserInteraction.AskFee account amount destination
        match maybeFee with
        | None -> ()
        | Some(fee) ->
            SendPaymentOfSpecificAmount account amount fee destination

let rec TryArchiveAccount account =
    let password = UserInteraction.AskPassword(false)
    try
        Account.Archive account password
        Console.WriteLine "Account archived."
        UserInteraction.PressAnyKeyToContinue ()
    with
    | :? InvalidPassword ->
        Presentation.Error "Invalid password, try again."
        TryArchiveAccount account

let rec AddReadOnlyAccounts() =
    Console.Write "JSON fragment from wallet to pair with: "
    let watchWalletInfoJson = Console.ReadLine().Trim()
    let watchWalletInfo = Marshalling.Deserialize watchWalletInfoJson
    let password = UserInteraction.AskPassword false
    Account.CreateReadOnlyAccounts watchWalletInfo password

let ArchiveAccount() =
    let account = UserInteraction.AskAccount()
    match account with
    | :? ReadOnlyAccount as readOnlyAccount ->
        Console.WriteLine("Read-only accounts cannot be archived, but just removed entirely.")
        if not (UserInteraction.AskYesNo "Do you accept?") then
            ()
        else
            Account.Remove readOnlyAccount
            Console.WriteLine "Read-only account removed."
            UserInteraction.PressAnyKeyToContinue()
    | :? NormalAccount as normalAccount ->
        let balance =
            Account.GetShowableBalance account ServerSelectionMode.Fast None
                |> Async.RunSynchronously
        match balance with
        | NotFresh(NotAvailable) ->
            Presentation.Error "Removing accounts when offline is not supported."
            ()
        | Fresh(amount) | NotFresh(Cached(amount,_)) ->
            if (amount > 0m) then
                Presentation.Error "Please empty the account before removing it."
                UserInteraction.PressAnyKeyToContinue ()
            else
                Console.WriteLine ()
                Console.WriteLine "Please note: "
                Console.WriteLine "Just in case this account receives funds in the future by mistake, "
                Console.WriteLine "the operation of archiving an account doesn't entirely remove it."
                Console.WriteLine ()
                Console.WriteLine "You will be asked the password of it now so that its private key can remain unencrypted in the configuration folder, in order for you to be able to safely forget this password."
                Console.WriteLine "Then this account will be watched constantly and if new payments are detected, "
                Console.WriteLine "GWallet will prompt you to move them to a current account without the need of typing the old password."
                Console.WriteLine ()
                if not (UserInteraction.AskYesNo "Do you accept?") then
                    ()
                else
                    TryArchiveAccount normalAccount
    | _ ->
        failwithf "Account type not valid for archiving: %s. Please report this issue."
                  (account.GetType().FullName)

let PairToWatchWallet() =
    let password = UserInteraction.AskPassword false
    match Account.GetNormalAccountsPairingInfoForWatchWallet password with
    | None ->
        Presentation.Error
            "There needs to be both Ether-based accounts and Utxo-based accounts to be able to use this feature."
    | Some walletInfo ->
        Console.WriteLine ""
        Console.WriteLine "Copy/paste this JSON fragment in your watching wallet:"
        Console.WriteLine ""
        let json = Marshalling.SerializeOneLine walletInfo
        Console.WriteLine json
        Console.WriteLine ""

    UserInteraction.PressAnyKeyToContinue()

type private GenericWalletOption =
    | Cancel
    | TestPaymentPassword
    | TestSeedPassphrase
    | WipeWallet

let rec TestPaymentPassword () =
    let password = UserInteraction.AskPassword false
    let passwordChecksOnAllAccounts = Account.CheckValidPassword password |> Async.RunSynchronously
    if not (passwordChecksOnAllAccounts.All(fun x -> x = true)) then
        Console.WriteLine "Try again."
        TestPaymentPassword ()

let rec TestSeedPassphrase(): unit =
    let passphrase,dob,email = UserInteraction.AskBrainSeed false
    let check = Account.CheckValidSeed passphrase dob email |> Async.RunSynchronously
    if not check then
        Console.WriteLine "Try again."
        TestSeedPassphrase()

let WipeWallet() =
    Console.WriteLine "If you want to remove accounts, the recommended way is to archive them, not wipe the whole wallet."
    Console.Write "Are you ABSOLUTELY SURE about this? If yes, write 'YES' in uppercase: "
    let sure = Console.ReadLine ()
    if sure = "YES" then
        Account.WipeAll()
    else
        ()

let WalletOptions(): unit =
    let rec AskWalletOption(): GenericWalletOption =
        Console.WriteLine "0. Cancel, go back"
        Console.WriteLine "1. Check you still remember your payment password"
        Console.WriteLine "2. Check you still remember your seed passphrase"
        Console.WriteLine "3. Wipe your current wallet, in order to start from scratch"
        Console.Write "Choose an option from the ones above: "
        let optIntroduced = Console.ReadLine ()
        match UInt32.TryParse optIntroduced with
        | false, _ -> AskWalletOption()
        | true, optionParsed ->
            match optionParsed with
            | 0u -> GenericWalletOption.Cancel
            | 1u -> GenericWalletOption.TestPaymentPassword
            | 2u -> GenericWalletOption.TestSeedPassphrase
            | 3u -> GenericWalletOption.WipeWallet
            | _ -> AskWalletOption()

    let walletOption = AskWalletOption()
    match walletOption with
    | GenericWalletOption.TestPaymentPassword ->
        TestPaymentPassword()
        Console.WriteLine "Success!"
    | GenericWalletOption.TestSeedPassphrase ->
        TestSeedPassphrase()
        Console.WriteLine "Success!"
    | GenericWalletOption.WipeWallet ->
        WipeWallet()
    | _ -> ()


let OptionFromMaybeCachedBalance (balance: MaybeCached<decimal>): Option<decimal> =
    match balance with
    | NotFresh NotAvailable ->
        Presentation.Error "Can't open channel while offline."
        None
    | Fresh balance | NotFresh (Cached(balance, _)) ->
        Some balance

// Alternative concise definition: http://www.fssnip.net/5Y/title/Repeat-until
let rec RetryOptionFunctionUntilSome (functionToRetry: unit -> Option<'T>): 'T =
    let returnedValue: Option<'T> = functionToRetry ()
    match returnedValue with
    | Some x -> x
    | None ->
        RetryOptionFunctionUntilSome functionToRetry

let BootstrapServerStats(): Async<unit> = async {
    if Config.BitcoinNet() = NBitcoin.Network.RegTest then
        return ()
    else
        return! Caching.Instance.BootstrapServerStatsFromTrustedSource()
}

let rec PerformOperation (accounts: seq<IAccount>): unit =
    match UserInteraction.AskOperation accounts with
    | Operations.Exit -> exit 0
    | Operations.CreateAccounts ->
        let bootstrapTask = BootstrapServerStats() |> Async.StartAsTask
        let passphrase,dob,email = UserInteraction.AskBrainSeed true
        if null <> bootstrapTask.Exception then
            raise bootstrapTask.Exception
        let masterPrivateKeyTask =
            Account.GenerateMasterPrivateKey passphrase dob email
                |> Async.StartAsTask
        let password = UserInteraction.AskPassword true
        Async.RunSynchronously <| async {
            let! privateKeyBytes = Async.AwaitTask masterPrivateKeyTask
            return! Account.CreateAllAccounts privateKeyBytes password
        }
        Console.WriteLine("Accounts created")
        UserInteraction.PressAnyKeyToContinue()
    | Operations.Refresh -> ()
    | Operations.SendPayment ->
        SendPayment()
    | Operations.AddReadonlyAccounts ->
        AddReadOnlyAccounts()
            |> Async.RunSynchronously
    | Operations.SignOffPayment ->
        SignOffPayment()
    | Operations.BroadcastPayment ->
        BroadcastPayment()
    | Operations.ArchiveAccount ->
        ArchiveAccount()
    | Operations.PairToWatchWallet ->
        PairToWatchWallet()
    | Operations.Options ->
        WalletOptions()
    | Operations.OpenChannel ->
        OpenChannel() |> Async.RunSynchronously
    | Operations.AcceptChannel ->
        AcceptChannel() |> Async.RunSynchronously
    | Operations.SendLightningPayment ->
        SendLightningPayment() |> Async.RunSynchronously
    | Operations.AcceptLightningEvent ->
        AcceptLightningEvent() |> Async.RunSynchronously
    | Operations.CloseChannel ->
        CloseChannel() |> Async.RunSynchronously
    | _ -> failwith "Unreachable"

let rec GetAccountOfSameCurrency currency =
    let account = UserInteraction.AskAccount()
    if (account.Currency <> currency) then
        Presentation.Error (sprintf "The account selected doesn't match the currency %A" currency)
        GetAccountOfSameCurrency currency
    else
        account

let rec CheckArchivedAccountsAreEmpty(): bool =
    let archivedAccountsInNeedOfAction =
        Account.GetArchivedAccountsWithPositiveBalance None
            |> Async.RunSynchronously
    for archivedAccount,balance in archivedAccountsInNeedOfAction do
        let currency = (archivedAccount:>IAccount).Currency
        Console.WriteLine (sprintf "ALERT! An archived account has received funds:%sAddress: %s Balance: %s%A"
                               Environment.NewLine
                               (archivedAccount:>IAccount).PublicAddress
                               (balance.ToString())
                               currency)
        Console.WriteLine "Please indicate the account you would like to transfer the funds to."
        let account = GetAccountOfSameCurrency currency

        let allBalance = TransferAmount(balance, balance, account.Currency)
        let maybeFee = UserInteraction.AskFee archivedAccount allBalance account.PublicAddress
        match maybeFee with
        | None -> ()
        | Some(feeInfo) ->
            let txId =
                Account.SweepArchivedFunds archivedAccount balance account feeInfo
                    |> Async.RunSynchronously
            Console.WriteLine(sprintf "Transaction successful, its ID is:%s%s" Environment.NewLine txId)
            UserInteraction.PressAnyKeyToContinue ()

    not (archivedAccountsInNeedOfAction.Any())

let rec ProgramMainLoop() =
    let accounts = Account.GetAllActiveAccounts()
    let channelStatusJobs: seq<Async<Async<seq<string>>>> = GetChannelStatuses accounts
    let revokedTxCheckJobs: seq<Async<Option<string>>> =
        ChainWatcher.CheckForChannelFraudsAndSendRevocationTx <| accounts.OfType<UtxoCoin.IUtxoAccount>()
    let revokedTxCheckJob: Async<array<Option<string>>> = Async.Parallel revokedTxCheckJobs
    let channelInfoInteractionsJob: Async<array<Async<seq<string>>>> = Async.Parallel channelStatusJobs
    let displayAccountStatusesJob =
        UserInteraction.DisplayAccountStatuses(WhichAccount.All accounts)
    let channelInfoInteractions, accountStatusesLines, _ =
        AsyncExtensions.MixedParallel3 channelInfoInteractionsJob displayAccountStatusesJob revokedTxCheckJob
        |> Async.RunSynchronously

    Console.WriteLine ()
    Console.WriteLine "*** STATUS ***"
    Console.WriteLine(String.concat Environment.NewLine accountStatusesLines)
    for channelInfoInteraction in channelInfoInteractions do
        let channelStatusLines =
            channelInfoInteraction |> Async.RunSynchronously
        Console.WriteLine(String.concat Environment.NewLine channelStatusLines)

    if CheckArchivedAccountsAreEmpty() then
        PerformOperation accounts

    ProgramMainLoop()


let NormalStartWithNoParameters () =

    Infrastructure.SetupSentryHook ()

    let exitCode =
        try
            ProgramMainLoop ()
            0
        with
        | ex ->
            Infrastructure.ReportCrash ex
            1

    exitCode


let UpdateServersFile () =
    ServerManager.UpdateServersFile()
    0

let UpdateServersStats () =
    ServerManager.UpdateServersStats()
    0

[<EntryPoint>]
let main argv =
    match argv.Length with
    | 0 ->
        Config.SetRunModeNormal()
        NormalStartWithNoParameters()
    | 2 when argv.[0] = "--regtest-on-localhost" ->
        Config.SetRunModeRegTest()
        NormalStartWithNoParameters()
    | 1 when argv.[0] = "--update-servers-file" ->
        Config.SetRunModeNormal()
        UpdateServersFile()
    | 1 when argv.[0] = "--update-servers-stats" ->
        Config.SetRunModeNormal()
        UpdateServersStats()
    | _ ->
        failwith "Arguments not recognized"
