open System
open System.Linq
open System.Text.RegularExpressions

open GWallet.Backend

type Options =
    | Exit               = 0
    | Refresh            = 1
    | CreateAccount      = 2
    | SendPayment        = 3
    | AddReadonlyAccount = 4
    | SignOffPayment     = 5
    | BroadcastPayment   = 6

let ConvertPascalCaseToSentence(pascalCaseElement: string) =
    Regex.Replace(pascalCaseElement, "[a-z][A-Z]",
                  (fun (m: Match) -> m.Value.[0].ToString() + " " + Char.ToLower(m.Value.[1]).ToString()))

exception NoOptionFound

let rec FindMatchingOption<'T> (optIntroduced, allOptions: ('T*int) list): 'T =
    match Int32.TryParse(optIntroduced) with
    | false, _ -> raise NoOptionFound
    | true, optionParsed ->
        match allOptions with
        | [] -> raise NoOptionFound
        | (head,i)::tail ->
            if (i = optionParsed) then
                head
            else
                FindMatchingOption(optIntroduced, tail)

let rec AskOption(numAccounts: int): Options =
    Console.WriteLine("Available options:")

    // TODO: move these 2 lines below to FSharpUtil?
    let allOptions = Enum.GetValues(typeof<Options>).Cast<Options>() |> List.ofSeq

    let allOptionsAvailable =
        seq {
            for option in allOptions do
                if not (option = Options.SendPayment && numAccounts = 0) then
                    Console.WriteLine(sprintf "%d: %s"
                                          (int option)
                                          (ConvertPascalCaseToSentence (option.ToString())))
                    yield option, int option
        } |> List.ofSeq
    Console.Write("Choose option to perform: ")
    let optIntroduced = System.Console.ReadLine()
    try
        FindMatchingOption(optIntroduced, allOptionsAvailable)
    with
    | :? NoOptionFound -> AskOption(numAccounts)

// taken from InfraLib
let ConsoleReadPasswordLine() =
    // taken from http://stackoverflow.com/questions/3404421/password-masking-console-application
    let rec ConsoleReadPasswordLineInternal(pwd: string) =
        let key = Console.ReadKey(true)

        if (key.Key = ConsoleKey.Enter) then
            Console.WriteLine()
            pwd
        else

            let newPwd =
                if (key.Key = ConsoleKey.Backspace && pwd.Length > 0) then
                    Console.Write("\b \b")
                    pwd.Substring(0, pwd.Length - 1)
                else
                    Console.Write("*")
                    pwd + key.KeyChar.ToString()
            ConsoleReadPasswordLineInternal(newPwd)

    ConsoleReadPasswordLineInternal(String.Empty)


let rec AskPassword(repeat: bool): string =
    Console.WriteLine()

    Console.Write("Write the password to unlock your account: ")
    let password = ConsoleReadPasswordLine()
    if not repeat then
        password
    else
        Console.Write("Repeat the password: ")
        let password2 = ConsoleReadPasswordLine()
        if (password <> password2) then
            Console.Error.WriteLine("Passwords are not the same, please try again.")
            AskPassword(repeat)
        else
            password

let rec AskCurrency(): Currency =
    Console.WriteLine()

    // TODO: move these 2 lines below to FSharpUtil?
    let allCurrencies = Enum.GetValues(typeof<Currency>).Cast<Currency>() |> List.ofSeq
    let allCurrenciesMappedToTheirIntValues = List.map (fun x -> (x, int x)) allCurrencies

    for option in allCurrencies do
        Console.WriteLine(sprintf "%d: %s" (int option) (option.ToString()))
    Console.Write("Select currency: ")
    let optIntroduced = System.Console.ReadLine()
    try
        FindMatchingOption(optIntroduced, allCurrenciesMappedToTheirIntValues)
    with
    | :? NoOptionFound -> AskCurrency()

let exchangeRateUnreachableMsg = " (USD exchange rate unreachable... offline?)"

type PublicAddress = string
type WhichAccount =
    All of seq<IAccount> | MatchingWith of IAccount

let DisplayAccountStatus accountNumber (account: IAccount) =
    let maybeReadOnly =
        match account with
        | :? ReadOnlyAccount -> "(READ-ONLY)"
        | _ -> String.Empty

    let accountInfo = sprintf "Account %d: %s%sCurrency=[%s] Address=[%s]"
                            accountNumber maybeReadOnly Environment.NewLine
                            (account.Currency.ToString())
                            account.PublicAddress
    Console.WriteLine(accountInfo)

    let balanceInUsdString balance maybeUsdValue =
        match maybeUsdValue with
        | NotFresh(NotAvailable) -> exchangeRateUnreachableMsg
        | Fresh(usdValue) ->
            sprintf "~ %s USD" ((balance * usdValue).ToString())
        | NotFresh(Cached(usdValue,time)) ->
            sprintf "~ %s USD (last known rate as of %s)"
                ((balance * usdValue).ToString())
                (time.ToString())

    let maybeUsdValue = FiatValueEstimation.UsdValue account.Currency

    let maybeBalance = AccountApi.GetBalance(account)
    match maybeBalance with
    | NotFresh(NotAvailable) ->
        Console.WriteLine("Unknown balance (Network unreachable... off-line?)")
    | NotFresh(Cached(balance,time)) ->
        let status = sprintf "Last known balance=[%s] (as of %s) %s %s"
                            (balance.ToString())
                            (time.ToString())
                            Environment.NewLine
                            (balanceInUsdString balance maybeUsdValue)
        Console.WriteLine(status)
    | Fresh(balance) ->
        let status = sprintf "Balance=[%s] %s"
                            (balance.ToString())
                            (balanceInUsdString balance maybeUsdValue)
        Console.WriteLine(status)

let DisplayAccountStatuses(whichAccount: WhichAccount) =
    match whichAccount with
    | WhichAccount.All(accounts) ->
        Console.WriteLine ()
        Console.WriteLine "*** STATUS ***"

        if (accounts.Any()) then
            for i = 0 to accounts.Count() - 1 do
                let account = accounts.ElementAt(i)
                DisplayAccountStatus (i+1) account
                Console.WriteLine ()
        else
            Console.WriteLine("No accounts have been created so far.")
        Console.WriteLine()

    | MatchingWith(account) ->
        let allAccounts =  AccountApi.GetAllAccounts()
        let matchFilter = (fun (acc:IAccount) -> acc.PublicAddress = account.PublicAddress &&
                                                 acc.Currency = account.Currency &&
                                                 acc :? NormalAccount)
        let accountsMatching = allAccounts.Where(matchFilter)
        if (accountsMatching.Count() <> 1) then
            failwith (sprintf
                            "account %s(%s) not found in config, or more than one with same public address?"
                            account.PublicAddress (account.Currency.ToString()))
        for i = 0 to allAccounts.Count() - 1 do
            let iterAccount = allAccounts.ElementAt(i)
            if (matchFilter (iterAccount)) then
                DisplayAccountStatus (i+1) iterAccount

let rec AskAccount(): IAccount =
    let allAccounts = AccountApi.GetAllAccounts()
    Console.Write("Write the account number: ")
    let accountNumber = Console.ReadLine()
    match Int32.TryParse(accountNumber) with
    | false, _ -> AskAccount()
    | true, accountParsed ->
        let theAccountChosen =
            try
                allAccounts.ElementAt(accountParsed - 1)
            with
            | _ -> AskAccount()
        theAccountChosen

let ETHEREUM_ADDRESSES_LENGTH = 42
let rec AskPublicAddress (askText: string) =
    Console.Write askText
    let publicAddress = Console.ReadLine()
    if not (publicAddress.StartsWith("0x")) then
        Console.Error.WriteLine("Error: address should start with '0x', please try again.")
        AskPublicAddress askText
    else if (publicAddress.Length <> ETHEREUM_ADDRESSES_LENGTH) then
        Console.Error.WriteLine(
            sprintf "Error: address should have a length of %d characters, please try again."
                ETHEREUM_ADDRESSES_LENGTH)
        AskPublicAddress askText
    else
        publicAddress

let rec AskAmount() =
    Console.Write("Amount of ether: ")
    let amount = Console.ReadLine()
    match Decimal.TryParse(amount) with
    | (false, _) ->
        Console.Error.WriteLine("Error: please enter a numeric amount")
        AskAmount()
    | (true, parsedAdmount) ->
        parsedAdmount

let rec AskAccept (): bool =
    Console.Write("Do you accept? (Y/N): ")
    let yesNoAnswer = Console.ReadLine().ToLowerInvariant()
    if (yesNoAnswer = "y") then
        true
    else if (yesNoAnswer = "n") then
        false
    else
        AskAccept()

let ShowFee currency (estimatedFee: EtherMinerFee) =
    let estimatedFeeInUsd =
        match FiatValueEstimation.UsdValue(currency) with
        | Fresh(usdValue) ->
            sprintf "(~%s USD)" ((usdValue * estimatedFee.EtherPriceForNormalTransaction).ToString())
        | NotFresh(Cached(usdValue,time)) ->
            sprintf "(~%s USD [last known rate at %s])"
                ((usdValue * estimatedFee.EtherPriceForNormalTransaction).ToString())
                (time.ToString())
        | NotFresh(NotAvailable) -> exchangeRateUnreachableMsg
    Console.WriteLine(sprintf "Estimated fee for this transaction would be:%s %s Ether %s"
                          Environment.NewLine
                          (estimatedFee.EtherPriceForNormalTransaction.ToString())
                          estimatedFeeInUsd
                     )

let AskFee(currency: Currency): Option<EtherMinerFee> =
    let estimatedFee = AccountApi.EstimateFee(currency)
    ShowFee currency estimatedFee
    let accept = AskAccept()
    if accept then
        Some(estimatedFee)
    else
        None

let rec TrySendAmount account destination amount fee =
    let password = AskPassword false
    try
        let txId = AccountApi.SendPayment account destination amount password fee
        Console.WriteLine(sprintf "Transaction successful, its ID is:%s%s" Environment.NewLine txId)
        Console.WriteLine()
    with
    | :? InsufficientFunds ->
        Console.Error.WriteLine("Insufficient funds")
    | :? InvalidPassword ->
        Console.Error.WriteLine("Invalid password, try again.")
        TrySendAmount account destination amount fee

let rec TrySign account unsignedTrans =
    let password = AskPassword false
    try
        AccountApi.SignUnsignedTransaction account unsignedTrans password
    with
    // TODO: would this throw insufficient funds? test
    //| :? InsufficientFunds ->
    //    Console.Error.WriteLine("Insufficient funds")
    | :? InvalidPassword ->
        Console.Error.WriteLine("Invalid password, try again.")
        TrySign account unsignedTrans

let ShowTransactionData trans =
    let maybeUsdPrice = FiatValueEstimation.UsdValue(trans.Proposal.Currency)
    let estimatedAmountInUsd =
        match maybeUsdPrice with
        | Fresh(usdPrice) ->
            Some(sprintf "~ %s USD" ((trans.Proposal.Amount * usdPrice).ToString()))
        | NotFresh(Cached(usdPrice, time)) ->
            Some(sprintf "~ %s USD (last exchange rate known at %s)"
                    ((trans.Proposal.Amount * usdPrice).ToString())
                    (time.ToString()))
        | NotFresh(NotAvailable) -> None

    Console.WriteLine("Transaction data:")
    Console.WriteLine("Sender: " + trans.Proposal.OriginAddress)
    Console.WriteLine("Recipient: " + trans.Proposal.DestinationAddress)
    Console.Write("Amount: " + trans.Proposal.Amount.ToString())
    if (estimatedAmountInUsd.IsSome) then
        Console.Write("  " + estimatedAmountInUsd.Value.ToString())
    Console.WriteLine()
    ShowFee trans.Proposal.Currency trans.Fee

let BroadcastPayment() =
    Console.Write("Introduce a file name to load the signed transaction: ")
    let filePathToReadFrom = Console.ReadLine()
    let signedTransaction = AccountApi.LoadSignedTransactionFromFile filePathToReadFrom
    //TODO: check if nonce matches, if not, reject trans

    // FIXME: we should be able to infer the trans info from the raw transaction! this way would be more secure too
    ShowTransactionData(signedTransaction.TransactionInfo)
    if AskAccept() then
        let txId = AccountApi.BroadcastTransaction signedTransaction
        Console.WriteLine(sprintf "Transaction successful, its ID is:%s%s" Environment.NewLine txId)
        Console.WriteLine()

let SignOffPayment() =
    Console.Write("Introduce a file name to load the unsigned transaction: ")
    let filePathToReadFrom = Console.ReadLine()
    let unsignedTransaction = AccountApi.LoadUnsignedTransactionFromFile filePathToReadFrom

    let accountsWithSameAddress =
        AccountApi.GetAllAccounts().Where(fun acc -> acc.PublicAddress = unsignedTransaction.Proposal.OriginAddress)
    if not (accountsWithSameAddress.Any()) then
        Console.Error.WriteLine("Error: The transaction doesn't correspond to any of the accounts in the wallet")
    else
        let accounts =
            accountsWithSameAddress.Where(
                fun acc -> acc.Currency = unsignedTransaction.Proposal.Currency &&
                           acc :? NormalAccount)
        if not (accounts.Any()) then
            Console.Error.WriteLine(
                sprintf
                    "Error: The transaction corresponds to an address of the accounts in this wallet, but it's a readonly account or it maps a different currency than %s"
                     (unsignedTransaction.Proposal.Currency.ToString()))
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
                DisplayAccountStatuses (WhichAccount.MatchingWith(account)) |> ignore
                Console.WriteLine()

                ShowTransactionData unsignedTransaction

                if AskAccept() then
                    let trans = TrySign normalAccount unsignedTransaction
                    Console.WriteLine("Transaction signed.")
                    Console.Write("Introduce a file name or path to save it: ")
                    let filePathToSaveTo = Console.ReadLine()
                    AccountApi.SaveSignedTransaction trans filePathToSaveTo
                    Console.WriteLine("Transaction signed and saved successfully. Now copy it to the online device.")    
            | _ ->
                failwith "Account type not supported. Please report this issue."
let rec PerformOptions(numAccounts: int) =
    match AskOption(numAccounts) with
    | Options.Exit -> exit 0
    | Options.CreateAccount ->
        let currency = AskCurrency()
        let password = AskPassword true
        let account = AccountApi.Create currency password
        Console.WriteLine("Account created: " + (account:>IAccount).PublicAddress)
    | Options.Refresh -> ()
    | Options.SendPayment ->
        let account = AskAccount()
        let destination = AskPublicAddress "Destination address: "
        let amount = AskAmount()
        let maybeFee = AskFee(account.Currency)
        match maybeFee with
        | None -> ()
        | Some(fee) ->
            match account with
            | :? ReadOnlyAccount as readOnlyAccount ->
                Console.WriteLine("Cannot send payments from readonly accounts.")
                Console.Write("Introduce a file name to save the unsigned transaction: ")
                let filePath = Console.ReadLine()
                let proposal = {
                    Currency = (readOnlyAccount:>IAccount).Currency;
                    OriginAddress = (readOnlyAccount:>IAccount).PublicAddress;
                    Amount = amount;
                    DestinationAddress = destination;
                }
                AccountApi.SaveUnsignedTransaction proposal fee filePath
                Console.WriteLine("Transaction saved. Now copy it to the device with the private key.")
            | :? NormalAccount as normalAccount ->
                TrySendAmount normalAccount destination amount fee
            | _ ->
                failwith ("Account type not recognized: " + account.GetType().FullName)
    | Options.AddReadonlyAccount ->
        let currency = AskCurrency()
        let accountPublicInfo = AskPublicAddress "Public address: "
        let roAccount = AccountApi.AddPublicWatcher currency accountPublicInfo
        ()
    | Options.SignOffPayment ->
        SignOffPayment()
    | Options.BroadcastPayment ->
        BroadcastPayment()
    | _ -> failwith "Unreachable"

let rec ProgramMainLoop() =
    let accounts = AccountApi.GetAllAccounts()
    DisplayAccountStatuses(WhichAccount.All(accounts))
    PerformOptions(accounts.Count())
    ProgramMainLoop()

[<EntryPoint>]
let main argv =
    ProgramMainLoop()

    0 // return an integer exit code
