open System
open System.Linq

open GWallet.Backend.FSharpUtil
open GWallet.Backend.UtxoCoin
open GWallet.Backend
open GWallet.Frontend.Console

let TrySendAmount (account: NormalAccount) transactionMetadata destination amount password =
    async {
        try
            let! txIdUri =
                Account.SendPayment account transactionMetadata destination amount password
            Console.WriteLine(sprintf "Transaction successful:%s%s" Environment.NewLine (txIdUri.ToString()))
            UserInteraction.PressAnyKeyToContinue ()
        with
        | :? DestinationEqualToOrigin ->
            Presentation.Error "Transaction's origin cannot be the same as the destination."
            UserInteraction.PressAnyKeyToContinue()
        | :? InsufficientFunds ->
            Presentation.Error "Insufficient funds."
            UserInteraction.PressAnyKeyToContinue()
    }

let TrySign account unsignedTrans password =
    async {
        //try
        return Account.SignUnsignedTransaction account unsignedTrans password
        //with
        // TODO: would this throw insufficient funds? test
        //| :? InsufficientFunds ->
        //    Presentation.Error "Insufficient funds."
    }

let BroadcastPayment() =
    let fileToReadFrom = UserInteraction.AskFileNameToLoad
                             "Introduce a file name to load the signed transaction: "
    let signedTransactionOpt =
        try
            Account.LoadSignedTransactionFromFile fileToReadFrom.FullName
            |> Some
        with
        | TransactionNotSignedYet ->
            None

    //TODO: check if nonce matches, if not, reject trans

    match signedTransactionOpt with
    | None ->
        Console.WriteLine String.Empty
        Presentation.Error "The transaction hasn't been signed yet."
        Console.WriteLine (
            sprintf
                "You maybe forgot to use the option '%s' on the offline device."
                (Presentation.ConvertPascalCaseToSentence (Operations.SignOffPayment.ToString()))
        )
        UserInteraction.PressAnyKeyToContinue ()

    | Some signedTransaction ->
        let transactionDetails = Account.GetSignedTransactionDetails signedTransaction
        Presentation.ShowTransactionData
            transactionDetails
            signedTransaction.TransactionInfo.Metadata

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
    let unsignedTransactionOpt =
        try
            let unsTx = Account.LoadUnsignedTransactionFromFile fileToReadFrom.FullName
            let accountsWithSameAddress =
                Account.GetAllActiveAccounts().Where(
                    fun acc -> acc.PublicAddress = unsTx.Proposal.OriginAddress
                )
            Some (unsTx, accountsWithSameAddress)
        with
        | TransactionAlreadySigned ->
            None

    match unsignedTransactionOpt with
    | None ->
        Console.WriteLine String.Empty
        Presentation.Error "The transaction is already signed."
        Console.WriteLine (
            sprintf
                "You maybe wanted to use the option '%s'."
                (Presentation.ConvertPascalCaseToSentence (Operations.BroadcastPayment.ToString()))
        )
        UserInteraction.PressAnyKeyToContinue ()

    | Some (_, accountsWithSameAddress) when not (accountsWithSameAddress.Any()) ->
        Presentation.Error "The transaction doesn't correspond to any of the accounts in the wallet."
        UserInteraction.PressAnyKeyToContinue ()

    | Some (unsignedTransaction, accountsWithSameAddress) ->
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

                Presentation.ShowTransactionData
                    (unsignedTransaction.Proposal :> ITransactionDetails)
                    unsignedTransaction.Metadata

                if UserInteraction.AskYesNo "Do you accept?" then
                    let trans =
                        TrySign normalAccount unsignedTransaction
                        |> UserInteraction.TryWithPasswordAsync
                        |> Async.RunSynchronously
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
        |> UserInteraction.TryWithPasswordAsync
        |> Async.RunSynchronously
    | _ ->
        failwith ("Account type not recognized: " + account.GetType().FullName)

let SendPayment() =
    let account = UserInteraction.AskAccount()
    let destination = UserInteraction.AskPublicAddress account.Currency "Destination address: "
    let maybeAmount = UserInteraction.AskAmount account true
    match maybeAmount with
    | None -> ()
    | Some(amount) ->
        let maybeFee = UserInteraction.AskFee account amount destination
        match maybeFee with
        | None -> ()
        | Some(fee) ->
            SendPaymentOfSpecificAmount account amount fee destination

let TryArchiveAccount account password =
    async {
        Account.Archive account password
        Console.WriteLine "Account archived."
        UserInteraction.PressAnyKeyToContinue ()
    }

let rec AddReadOnlyAccounts() =
    Console.Write "JSON fragment from wallet to pair with: "
    let watchWalletInfoJson = Console.ReadLine().Trim()
    let watchWalletInfoOpt =
        try
            Marshalling.Deserialize watchWalletInfoJson
            |> Some
        with
        | InvalidJson ->
            None

    match watchWalletInfoOpt with
    | Some watchWalletInfo ->
        Account.CreateReadOnlyAccounts watchWalletInfo
        |> Some
    | None ->
        Console.WriteLine String.Empty
        Presentation.Error
            "The input provided didn't have proper JSON structure. Are you sure you gathered the info properly?"
        Console.WriteLine (
            sprintf
                "You have to choose the option '%s' in your offline device to obtain the JSON."
                (Presentation.ConvertPascalCaseToSentence (Operations.PairToWatchWallet.ToString()))
        )
        UserInteraction.PressAnyKeyToContinue()
        None

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
                    |> UserInteraction.TryWithPasswordAsync
                    |> Async.RunSynchronously
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
    if Account.CheckValidPassword password None |> Async.RunSynchronously then
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
        Console.WriteLine "2. Check you still remember your secret recovery phrase"
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

let rec PerformOperation (activeAccounts: seq<IAccount>): unit =
    match UserInteraction.AskOperation activeAccounts with
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
        match AddReadOnlyAccounts() with
        | Some job ->
            job
            |> Async.RunSynchronously
        | None -> ()
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
        LayerTwo.OpenChannel() |> Async.RunSynchronously
    | Operations.AcceptChannel ->
        LayerTwo.AcceptChannel() |> Async.RunSynchronously
    | Operations.SendLightningPayment ->
        LayerTwo.SendHtlcPayment() |> Async.RunSynchronously
    | Operations.CreateInvoice ->
        LayerTwo.CreateInvoice() |> Async.RunSynchronously
    | Operations.ReceiveLightningEvent ->
        LayerTwo.ReceiveLightningEvent() |> Async.RunSynchronously
    | Operations.CloseChannel ->
        LayerTwo.CloseChannel() |> Async.RunSynchronously
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
    let activeAccounts = Account.GetAllActiveAccounts()

    let channelStatusJobs: seq<Async<unit -> Async<seq<string>>>> = LayerTwo.GetChannelStatuses activeAccounts
    let revokedTxCheckJobs: seq<Async<Option<string>>> =
        Lightning.ChainWatcher.CheckForChannelFraudsAndSendRevocationTx
        <| activeAccounts.OfType<UtxoCoin.NormalUtxoAccount>()
    let revokedTxCheckJob: Async<array<Option<string>>> = Async.Parallel revokedTxCheckJobs
    let channelInfoInteractionsJob: Async<array<unit -> Async<seq<string>>>> = Async.Parallel channelStatusJobs
    let displayAccountStatusesJob =
        UserInteraction.DisplayAccountStatuses(WhichAccount.All activeAccounts)
    let gossipSyncJob = Lightning.RapidGossipSyncer.Sync() 
    let channelInfoInteractions, accountStatusesLines, _, _ =
        AsyncExtensions.MixedParallel4 channelInfoInteractionsJob displayAccountStatusesJob revokedTxCheckJob gossipSyncJob
        |> Async.RunSynchronously

    let unresolvedHtlcsCheckJobs: seq<Async<bool>> =
        Lightning.ChainWatcher.CheckForForceCloseAndSaveUnresolvedHtlcs
        <| activeAccounts.OfType<UtxoCoin.NormalUtxoAccount>()
    let unresolvedHtlcsCheckJob: Async<array<bool>> = Async.Parallel unresolvedHtlcsCheckJobs
    unresolvedHtlcsCheckJob |> Async.RunSynchronously |> ignore<array<bool>>

    LayerTwo.HandleReadyToResolveHtlc activeAccounts
    |> Async.RunSynchronously

    LayerTwo.HandleReadyToRecoverDelayedHtlcs activeAccounts
    |> Async.RunSynchronously

    //Rerun unresolved check
    unresolvedHtlcsCheckJob |> Async.RunSynchronously |> ignore<array<bool>>

    Console.WriteLine ()
    Console.WriteLine "*** STATUS ***"
    Console.WriteLine(String.concat Environment.NewLine accountStatusesLines)

    Console.WriteLine()

    let rec runChannelInteractions (statusLines: seq<string>) (channelInfoInteractions: seq<unit -> Async<seq<string>>>) =
        match Seq.tryHead channelInfoInteractions with
        | Some headInteraction ->
            let channelStatusLines =
                headInteraction ()
                |> Async.RunSynchronously
            runChannelInteractions (Seq.append statusLines channelStatusLines) (Seq.tail channelInfoInteractions)
        | None ->
            statusLines

    let channelStatuses = runChannelInteractions Seq.empty channelInfoInteractions

    Console.WriteLine ()
    Console.WriteLine (String.concat Environment.NewLine channelStatuses)
    Console.WriteLine ()

    if CheckArchivedAccountsAreEmpty() then
        PerformOperation activeAccounts

    ProgramMainLoop()


let NormalStartWithNoParameters () =

    Infrastructure.SetupExceptionHook ()

    let exitCode =
        try
            ProgramMainLoop ()
            0
        with
        | ex ->
            Infrastructure.LogOrReportCrash ex
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
    | 1 when argv.[0] = "--version" ->
        Console.WriteLine (sprintf "geewallet v%s" VersionHelper.CURRENT_VERSION)
        0
    | 1 when argv.[0] = "--regtest-on-localhost" ->
#if DEBUG
        Config.SetRunModeToTesting()
        NormalStartWithNoParameters()
#else
        failwith "Regtest was requested on command line but Geewallet can only use regtest when compiled with -p:Configuration=Debug"
#endif
    | 1 when argv.[0] = "--update-servers-file" ->
        UpdateServersFile()
    | 1 when argv.[0] = "--update-servers-stats" ->
        UpdateServersStats()
    | _ ->
        failwith "Arguments not recognized"
