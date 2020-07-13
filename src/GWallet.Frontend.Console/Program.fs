open System
open System.IO
open System.Linq
open System.Text.RegularExpressions
open System.Net

open DotNetLightning.Utils
open GWallet.Backend
open GWallet.Backend.FSharpUtil
open GWallet.Backend.UtxoCoin.Lightning
open GWallet.Frontend.Console

let random = Org.BouncyCastle.Security.SecureRandom () :> Random

let BindLightning (account: UtxoCoin.NormalUtxoAccount)
                  (password: string)
                       : TransportListener =
    let nodeSecret = Lightning.GetLightningNodeSecret account password
    let ipEndpoint = UserInteraction.AskBindAddress()
    TransportListener.Bind nodeSecret ipEndpoint

let OpenChannel(): Async<unit> = async {
    let account = UserInteraction.AskBitcoinAccount()

    match UserInteraction.AskAmount account with
    | None -> return ()
    | Some channelCapacity ->
        match UserInteraction.AskChannelCounterpartyConnectionDetails() with
        | None -> return ()
        | Some (ipEndPoint, pubKey) ->
            DebugLogger "Calling EstimateFee..."
            let metadata =
                // this dummy address is only used for fee estimation
                let witScriptIdLength = 32
                let nullScriptId = NBitcoin.WitScriptId (Array.zeroCreate witScriptIdLength)
                let dummyAddr = NBitcoin.BitcoinWitScriptAddress (nullScriptId, Config.BitcoinNet)

                try
                    UtxoCoin.Account.EstimateFeeForDestination
                         account channelCapacity dummyAddr
                         |> Async.RunSynchronously
                with
                | InsufficientBalanceForFee _ ->
                    failwith "Estimated fee is too high for the remaining balance, \
                              use a different account or a different amount."
            Presentation.ShowFeeAndSpendableBalance metadata channelCapacity

            let acceptFeeRate = UserInteraction.AskYesNo "Do you accept?"
            if acceptFeeRate then
                let password = UserInteraction.AskPassword false
                let nodeSecret = Lightning.GetLightningNodeSecret account password
                let peerNodeId = DotNetLightning.Utils.Primitives.NodeId pubKey
                let peerId = DotNetLightning.Utils.Primitives.PeerId (ipEndPoint :> EndPoint)
                let! connectRes = 
                    PeerWrapper.Connect nodeSecret peerNodeId peerId
                match connectRes with
                | FSharp.Core.Error connectError ->
                    Console.WriteLine (sprintf "Error connecting to peer: %s" connectError.Message)
                    if connectError.PossibleBug then
                        let msg =
                            sprintf
                                "Error connecting to peer:%s@%s:%i: %s"
                                (pubKey.ToString())
                                (ipEndPoint.Address.ToString())
                                ipEndPoint.Port
                                connectError.Message
                        Infrastructure.ReportWarningMessage msg
                | FSharp.Core.Ok peerWrapper ->
                    let! outgoingUnfundedChannelRes =
                        OutgoingUnfundedChannel.OpenChannel
                            peerWrapper
                            account
                            channelCapacity
                            metadata
                            password

                    match outgoingUnfundedChannelRes with
                    | FSharp.Core.Error openChannelError ->
                        Console.WriteLine(sprintf "Error opening channel: %s" openChannelError.Message)
                        if openChannelError.PossibleBug then
                            let msg =
                                sprintf
                                    "Error opening channel with peer:%s@%s:%i: %s"
                                    (pubKey.ToString())
                                    (ipEndPoint.Address.ToString())
                                    ipEndPoint.Port
                                    openChannelError.Message
                            Infrastructure.ReportWarningMessage msg
                    | FSharp.Core.Ok outgoingUnfundedChannel ->
                        Console.WriteLine(
                            sprintf
                                "Opening a channel with this party will require %i confirmations (~%i minutes)"
                                outgoingUnfundedChannel.MinimumDepth.Value
                                (outgoingUnfundedChannel.MinimumDepth.Value * 10u)
                        )
                        let acceptMinimumDepth = UserInteraction.AskYesNo "Do you accept?"
                        if acceptMinimumDepth then
                            let! fundedChannelRes = FundedChannel.FundChannel outgoingUnfundedChannel
                            match fundedChannelRes with
                            | FSharp.Core.Error fundChannelError ->
                                Console.WriteLine(sprintf "Error funding channel: %s" fundChannelError.Message)
                                if fundChannelError.PossibleBug then
                                    let msg =
                                        sprintf
                                            "Error funding channel with peer:%s@%s:%i: %s"
                                            (pubKey.ToString())
                                            (ipEndPoint.Address.ToString())
                                            ipEndPoint.Port
                                            fundChannelError.Message
                                    Infrastructure.ReportWarningMessage msg
                            | FSharp.Core.Ok fundedChannel ->
                                let txId = fundedChannel.FundingTxId
                                let uri = BlockExplorer.GetTransaction Currency.BTC (txId.Value.ToString())
                                Console.WriteLine(sprintf "A funding transaction was broadcast: %A" uri)
                                (fundedChannel :> IDisposable).Dispose()
            UserInteraction.PressAnyKeyToContinue()
}

let AcceptChannel(): Async<unit> = async {
    let account = UserInteraction.AskBitcoinAccount()
    let password = UserInteraction.AskPassword false
    let transportListener = BindLightning account password

    let publicKey = transportListener.PublicKey
    let ipEndPoint = transportListener.LocalEndpoint
    Console.WriteLine(
        sprintf
            "This node, connect to it: %s@%s"
            (publicKey.ToString())
            (ipEndPoint.ToString())
    )
    let! acceptPeerRes = PeerWrapper.AcceptAnyFromTransportListener transportListener
    match acceptPeerRes with
    | FSharp.Core.Error connectError ->
        Console.WriteLine(sprintf "Error accepting connection from peer: %s" connectError.Message)
        if connectError.PossibleBug then
            let msg =
                sprintf
                    "Error accepting connection: %s"
                    connectError.Message
            Infrastructure.ReportWarningMessage msg
    | FSharp.Core.Ok peerWrapper ->
        let! fundedChannelRes = FundedChannel.AcceptChannel peerWrapper account
        match fundedChannelRes with
        | FSharp.Core.Error acceptChannelError ->
            Console.WriteLine(sprintf "Error accepting channel: %s" acceptChannelError.Message)
            if acceptChannelError.PossibleBug then
                let msg =
                    sprintf
                        "Error accepting channel from peer:%s@%s:%i: %s"
                        (peerWrapper.RemoteNodeId.Value.ToString())
                        (peerWrapper.RemoteEndPoint.Address.ToString())
                        peerWrapper.RemoteEndPoint.Port
                        acceptChannelError.Message
                Infrastructure.ReportWarningMessage msg
        | FSharp.Core.Ok fundedChannel ->
            Console.WriteLine(sprintf "Channel opened. Txid: %s" (fundedChannel.FundingTxId.ToString()))
            Console.WriteLine "Waiting for funding locked."
            (fundedChannel :> IDisposable).Dispose()
    Lightning.StopLightning transportListener
    UserInteraction.PressAnyKeyToContinue()
}

let SendLightningPayment(): Async<unit> = async {
    let channelIdOpt = UserInteraction.AskChannelId true
    match channelIdOpt with
    | None -> return ()
    | Some channelId ->
        let amountOpt = option {
            let! transferAmount = UserInteraction.AskLightningAmount channelId
            let btcAmount = transferAmount.ValueToSend
            let lnAmount = int64(btcAmount * decimal DotNetLightning.Utils.LNMoneyUnit.BTC)
            return DotNetLightning.Utils.LNMoney lnAmount
        }
        match amountOpt with
        | None -> ()
        | Some amount ->
            let account = Lightning.GetLightningChannelAccount channelId
            let password = UserInteraction.AskPassword false
            let nodeSecret = Lightning.GetLightningNodeSecret account password
            let! connectRes = ActiveChannel.ConnectReestablish nodeSecret channelId
            match connectRes with
            | FSharp.Core.Error connectError ->
                Console.WriteLine(sprintf "Error reestablishing channel: %s" connectError.Message)
                if connectError.PossibleBug then
                    let msg =
                        sprintf
                            "Error reestablishing channel %s to send payment: %s"
                            (channelId.Value.ToString())
                            connectError.Message
                    Infrastructure.ReportWarningMessage msg
            | FSharp.Core.Ok activeChannel ->
                let! paymentRes = activeChannel.SendMonoHopUnidirectionalPayment amount
                match paymentRes with
                | FSharp.Core.Error paymentError ->
                    Console.WriteLine(sprintf "Error sending monohop payment: %s" paymentError.Message)
                    if paymentError.PossibleBug then
                        let msg =
                            sprintf
                                "Error sending payment on channel %s: %s"
                                (channelId.Value.ToString())
                                paymentError.Message
                        Infrastructure.ReportWarningMessage msg
                | FSharp.Core.Ok activeChannel ->
                    (activeChannel :> IDisposable).Dispose()
                    Console.WriteLine "Payment sent."
            UserInteraction.PressAnyKeyToContinue()
}

let ReceiveLightningPayment(): Async<unit> = async {
    let channelIdOpt = UserInteraction.AskChannelId false
    match channelIdOpt with
    | None -> return ()
    | Some channelId ->
        let account = Lightning.GetLightningChannelAccount channelId
        let password = UserInteraction.AskPassword false
        let transportListener = BindLightning account password
        let! connectRes = ActiveChannel.AcceptReestablish transportListener channelId
        match connectRes with
        | FSharp.Core.Error connectError ->
            Console.WriteLine(sprintf "Error reestablishing channel: %s" connectError.Message)
            if connectError.PossibleBug then
                let msg =
                    sprintf
                        "Error reestablishing channel %s to receive payment: %s"
                        (channelId.Value.ToString())
                        connectError.Message
                Infrastructure.ReportWarningMessage msg
        | FSharp.Core.Ok activeChannel ->
            let! paymentRes = activeChannel.RecvMonoHopUnidirectionalPayment()
            match paymentRes with
            | FSharp.Core.Error paymentError ->
                Console.WriteLine(sprintf "Error receiving monohop payment: %s" paymentError.Message)
                if paymentError.PossibleBug then
                    let msg =
                        sprintf
                            "Error receiving payment on channel %s: %s"
                            (channelId.Value.ToString())
                            paymentError.Message
                    Infrastructure.ReportWarningMessage msg
            | FSharp.Core.Ok activeChannel ->
                (activeChannel :> IDisposable).Dispose()
                Console.WriteLine "Payment received."
        Lightning.StopLightning transportListener
        UserInteraction.PressAnyKeyToContinue()
}

let InitiateCloseLightningChannel(): Async<unit> = async {
    let channelIdOpt = UserInteraction.AskAnyChannelId
    match channelIdOpt with
    | None -> return ()
    | Some channelId ->
        let account = Lightning.GetLightningChannelAccount channelId
        let password = UserInteraction.AskPassword false
        let nodeSecret = Lightning.GetLightningNodeSecret account password
        let! connectRes = ActiveChannel.ConnectReestablish nodeSecret channelId
        match connectRes with
        | FSharp.Core.Error connectError ->
            Console.WriteLine(sprintf "Error reestablishing channel: %s" connectError.Message)
            if connectError.PossibleBug then
                let msg =
                    sprintf
                        "Error reestablishing channel %s to receive payment: %s"
                        (channelId.Value.ToString())
                        connectError.Message
                Infrastructure.ReportWarningMessage msg
        | FSharp.Core.Ok activeChannel ->
            let! closeRes = ClosedChannel.InitiateCloseChannel activeChannel.ConnectedChannel

            match closeRes with
            | FSharp.Core.Error closeError ->
                Console.WriteLine(sprintf "Error closing channel: %s" closeError.Message)
            | FSharp.Core.Ok _ ->
                Console.WriteLine "Channel closed."
        UserInteraction.PressAnyKeyToContinue()
}

let AwaitCloseLightningChannel(): Async<unit> = async {
    let channelIdOpt = UserInteraction.AskChannelId false
    match channelIdOpt with
    | None -> return ()
    | Some channelId ->
        let account = Lightning.GetLightningChannelAccount channelId
        let password = UserInteraction.AskPassword false
        let transportListener = BindLightning account password
        let! connectRes = ActiveChannel.AcceptReestablish transportListener channelId
        match connectRes with
        | FSharp.Core.Error connectError ->
            Console.WriteLine(sprintf "Error reestablishing channel: %s" connectError.Message)
            if connectError.PossibleBug then
                let msg =
                    sprintf
                        "Error reestablishing channel %s to close channel payment: %s"
                        (channelId.Value.ToString())
                        connectError.Message
                Infrastructure.ReportWarningMessage msg
        | FSharp.Core.Ok activeChannel ->
            let! closeRes = ClosedChannel.AwaitCloseChannel activeChannel.ConnectedChannel

            match closeRes with
            | FSharp.Core.Error closeError ->
                Console.WriteLine(sprintf "Error closing channel: %s" closeError.Message)
            | FSharp.Core.Ok _ ->
                Console.WriteLine "Channel closed."
        Lightning.StopLightning transportListener
        UserInteraction.PressAnyKeyToContinue()
}

let LockChannelsIfFundingConfirmed(): Async<unit> = async {
    let channelIds = List.ofSeq (SerializedChannel.ListSavedChannels())
    for channelId in channelIds do
        let serializedChannel = SerializedChannel.LoadFromWallet channelId
        let! status = Lightning.GetSerializedChannelStatus serializedChannel
        match status with
        | Lightning.ChannelStatus.FundingConfirmed ->
            Console.WriteLine(sprintf "Funding for channel %s confirmed" (channelId.ToString()))
            Console.WriteLine "In order to continue the funding for the channel needs to be locked"
            let account = Lightning.GetLightningChannelAccount channelId
            let password = UserInteraction.AskPassword false
            let! activeChannelRes =
                if serializedChannel.IsFunder then
                    Console.WriteLine
                        "Ensure the fundee is ready to accept a connection to lock the funding, \
                        then press any key to continue."
                    Console.ReadKey true |> ignore
                    let nodeSecret = Lightning.GetLightningNodeSecret account password
                    ActiveChannel.ConnectReestablish nodeSecret channelId
                else
                    Console.WriteLine "Listening for connection from peer"
                    let transportListener = BindLightning account password
                    ActiveChannel.AcceptReestablish transportListener channelId
            match activeChannelRes with
            | FSharp.Core.Ok activeChannel ->
                (activeChannel :> IDisposable).Dispose()
                Console.WriteLine(sprintf "funding locked for channel %s" (channelId.ToString()))
            | FSharp.Core.Error connectError ->
                Console.WriteLine(sprintf "Error reestablishing channel: %s" connectError.Message)
                if connectError.PossibleBug then
                    let msg =
                        sprintf
                            "Error reestablishing channel %s to lock funding: %s"
                            (channelId.Value.ToString())
                            connectError.Message
                    Infrastructure.ReportWarningMessage msg
        | _ -> ()
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
    Account.CreateReadOnlyAccounts watchWalletInfo

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
    match Account.GetNormalAccountsPairingInfoForWatchWallet() with
    | None ->
        Presentation.Error
            "There needs to be both Ether-based accounts and Utxo-based accounts to be able to use this feature."
    | Some walletInfo ->
        Console.WriteLine ""
        Console.WriteLine "Copy/paste this JSON fragment in your watching wallet:"
        Console.WriteLine ""
        let json = Marshalling.Serialize walletInfo
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

let rec PerformOperation (numAccounts: int): unit =
    match UserInteraction.AskOperation numAccounts with
    | Operations.Exit -> exit 0
    | Operations.CreateAccounts ->
        let bootstrapTask = Caching.Instance.BootstrapServerStatsFromTrustedSource() |> Async.StartAsTask
        let passphrase,dob,email = UserInteraction.AskBrainSeed true
        if null <> bootstrapTask.Exception then
            raise bootstrapTask.Exception
        let masterPrivateKeyTask =
            Account.GenerateMasterPrivateKey passphrase dob email
                |> Async.StartAsTask
        let password = UserInteraction.AskPassword true
        Async.RunSynchronously (Account.CreateAllAccounts masterPrivateKeyTask password)
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
    | Operations.ReceiveLightningPayment ->
        ReceiveLightningPayment() |> Async.RunSynchronously
    | Operations.InitiateCloseChannel ->
        InitiateCloseLightningChannel() |> Async.RunSynchronously
    | Operations.AwaitCloseChannel ->
        AwaitCloseLightningChannel() |> Async.RunSynchronously
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
    Console.WriteLine ()
    Console.WriteLine "*** STATUS ***"
    let lines = seq {
        yield!
            UserInteraction.DisplayAccountStatuses(WhichAccount.All accounts)
                |> Async.RunSynchronously
        yield! UserInteraction.DisplayLightningChannelStatuses()
    }
    Console.WriteLine(String.concat Environment.NewLine lines)

    LockChannelsIfFundingConfirmed()
        |> Async.RunSynchronously

    if CheckArchivedAccountsAreEmpty() then
        PerformOperation (accounts.Count())

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
        NormalStartWithNoParameters()
    | 1 when argv.[0] = "--update-servers-file" ->
        UpdateServersFile()
    | 1 when argv.[0] = "--update-servers-stats" ->
        UpdateServersStats()
    | _ ->
        failwith "Arguments not recognized"
