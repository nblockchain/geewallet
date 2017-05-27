open System
open System.Linq
open System.Text.RegularExpressions

open GWallet.Backend
open GWallet.Frontend.Console

type PublicAddress = string

let rec TrySendAmount account destination amount fee =
    let password = UserInteraction.AskPassword false
    try
        let txId = AccountApi.SendPayment account destination amount password fee
        Console.WriteLine(sprintf "Transaction successful, its ID is:%s%s" Environment.NewLine txId)
        UserInteraction.PressAnyKeyToContinue ()
    with
    | :? InsufficientFunds ->
        Presentation.Error "Insufficient funds."
        UserInteraction.PressAnyKeyToContinue()
    | :? InvalidPassword ->
        Presentation.Error "Invalid password, try again."
        TrySendAmount account destination amount fee

let rec TrySign account unsignedTrans =
    let password = UserInteraction.AskPassword false
    try
        AccountApi.SignUnsignedTransaction account unsignedTrans password
    with
    // TODO: would this throw insufficient funds? test
    //| :? InsufficientFunds ->
    //    Presentation.Error "Insufficient funds."
    | :? InvalidPassword ->
        Presentation.Error "Invalid password, try again."
        TrySign account unsignedTrans

let BroadcastPayment() =
    Console.Write("Introduce a file name to load the signed transaction: ")
    let filePathToReadFrom = Console.ReadLine()
    let signedTransaction = AccountApi.LoadSignedTransactionFromFile filePathToReadFrom
    //TODO: check if nonce matches, if not, reject trans

    // FIXME: we should be able to infer the trans info from the raw transaction! this way would be more secure too
    Presentation.ShowTransactionData(signedTransaction.TransactionInfo)
    if UserInteraction.AskAccept() then
        let txId = AccountApi.BroadcastTransaction signedTransaction
        Console.WriteLine(sprintf "Transaction successful, its ID is:%s%s" Environment.NewLine txId)
        UserInteraction.PressAnyKeyToContinue ()

let SignOffPayment() =
    Console.Write("Introduce a file name to load the unsigned transaction: ")
    let filePathToReadFrom = Console.ReadLine()
    let unsignedTransaction = AccountApi.LoadUnsignedTransactionFromFile filePathToReadFrom

    let accountsWithSameAddress =
        AccountApi.GetAllAccounts().Where(fun acc -> acc.PublicAddress = unsignedTransaction.Proposal.OriginAddress)
    if not (accountsWithSameAddress.Any()) then
        Presentation.Error "The transaction doesn't correspond to any of the accounts in the wallet."
        UserInteraction.PressAnyKeyToContinue ()
    else
        let accounts =
            accountsWithSameAddress.Where(
                fun acc -> acc.Currency = unsignedTransaction.Proposal.Currency &&
                           acc :? NormalAccount)
        if not (accounts.Any()) then
            Presentation.Error(
                sprintf
                    "The transaction corresponds to an address of the accounts in this wallet, but it's a readonly account or it maps a different currency than %s."
                     (unsignedTransaction.Proposal.Currency.ToString()))
            UserInteraction.PressAnyKeyToContinue()
        else
            let account = accounts.First()
            if (accounts.Count() > 1) then
                failwith "More than one normal account matching address and currency? Please report this issue."

            match account with
            | :? ReadOnlyAccount as readOnlyAccount ->
                failwith "Previous account filtering should have discarded readonly accounts already. Please report this issue"
            | :? NormalAccount as normalAccount ->
                Console.WriteLine ("Importing external data...")
                Caching.SaveSnapshot unsignedTransaction.Cache

                Console.WriteLine ("Account to use when signing off this transaction:")
                Console.WriteLine ()
                UserInteraction.DisplayAccountStatuses (WhichAccount.MatchingWith(account)) |> ignore
                Console.WriteLine()

                Presentation.ShowTransactionData unsignedTransaction

                if UserInteraction.AskAccept() then
                    let trans = TrySign normalAccount unsignedTransaction
                    Console.WriteLine("Transaction signed.")
                    Console.Write("Introduce a file name or path to save it: ")
                    let filePathToSaveTo = Console.ReadLine()
                    AccountApi.SaveSignedTransaction trans filePathToSaveTo
                    Console.WriteLine("Transaction signed and saved successfully. Now copy it to the online device.")
                    UserInteraction.PressAnyKeyToContinue ()
            | _ ->
                failwith "Account type not supported. Please report this issue."

let SendPaymentOfSpecificAmount (account: IAccount) amount fee =
    let destination = UserInteraction.AskPublicAddress "Destination address: "
    match account with
    | :? ReadOnlyAccount as readOnlyAccount ->
        Console.WriteLine("Cannot send payments from readonly accounts.")
        Console.Write("Introduce a file name to save the unsigned transaction: ")
        let filePath = Console.ReadLine()
        let proposal = {
            Currency = account.Currency;
            OriginAddress = account.PublicAddress;
            Amount = amount;
            DestinationAddress = destination;
        }
        AccountApi.SaveUnsignedTransaction proposal fee filePath
        Console.WriteLine("Transaction saved. Now copy it to the device with the private key.")
        UserInteraction.PressAnyKeyToContinue()
    | :? NormalAccount as normalAccount ->
        TrySendAmount normalAccount destination amount fee
    | _ ->
        failwith ("Account type not recognized: " + account.GetType().FullName)

let SendPayment() =
    let account = UserInteraction.AskAccount()
    let maybeFee = UserInteraction.AskFee(account.Currency)
    match maybeFee with
    | None -> ()
    | Some(fee) ->
        let amount = UserInteraction.AskAmount account
        match amount with
        | UserInteraction.AmountToTransfer.CancelOperation -> ()
        | UserInteraction.AmountToTransfer.CertainCryptoAmount(amount) ->
            SendPaymentOfSpecificAmount account amount fee
        | UserInteraction.AmountToTransfer.AllBalance(allBalance) ->
            SendPaymentOfSpecificAmount account (allBalance - fee.EtherPriceForNormalTransaction) fee

let rec PerformOptions(numAccounts: int) =
    match UserInteraction.AskOption(numAccounts) with
    | Options.Exit -> exit 0
    | Options.CreateAccount ->
        let currency = UserInteraction.AskCurrency()
        let password = UserInteraction.AskPassword true
        let account = AccountApi.Create currency password
        Console.WriteLine("Account created: " + (account:>IAccount).PublicAddress)
        UserInteraction.PressAnyKeyToContinue()
    | Options.Refresh -> ()
    | Options.SendPayment ->
        SendPayment()
    | Options.AddReadonlyAccount ->
        let currency = UserInteraction.AskCurrency()
        let accountPublicInfo = UserInteraction.AskPublicAddress "Public address: "
        let roAccount = AccountApi.AddPublicWatcher currency accountPublicInfo
        ()
    | Options.SignOffPayment ->
        SignOffPayment()
    | Options.BroadcastPayment ->
        BroadcastPayment()
    | _ -> failwith "Unreachable"

let rec ProgramMainLoop() =
    let accounts = AccountApi.GetAllAccounts()
    UserInteraction.DisplayAccountStatuses(WhichAccount.All(accounts))
    PerformOptions(accounts.Count())
    ProgramMainLoop()

[<EntryPoint>]
let main argv =

    // workaround for needing to use non-Portable version of BouncyCastle's nuget
    AppDomain.CurrentDomain.add_AssemblyResolve (
        ResolveEventHandler (fun _ args ->
            if (args.Name = "crypto, Version=1.8.1.0, Culture=neutral, PublicKeyToken=0e99375e54769942") then
                typedefof<Org.BouncyCastle.Security.SecureRandom>.Assembly
            else
                null))

    ProgramMainLoop()

    0 // return an integer exit code
