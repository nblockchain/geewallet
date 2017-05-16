open System
open System.Linq
open System.Text.RegularExpressions

open GWallet.Backend

type Options =
    | Exit          = 0
    | Refresh       = 1
    | CreateAccount = 2
    | SendPayment   = 3

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
    Console.WriteLine()
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

let DisplayStatus() =
    Console.WriteLine("** STATUS **")
    let accounts = AccountApi.GetAllAccounts()
    if (accounts.Any()) then
        for i = 0 to accounts.Count() - 1 do
            let account = accounts.ElementAt(i)
            let accountInfo = sprintf "Account %d:%sCurrency=[%s] Address=[%s]"
                                  (i+1) Environment.NewLine
                                  (account.Currency.ToString())
                                  account.PublicAddress
            Console.WriteLine(accountInfo)

            let maybeBalance = AccountApi.GetBalance(account)
            match maybeBalance with
            | None ->
                Console.WriteLine("Unknown balance (Network unreachable... off-line?)")
            | Some(balance) ->
                let maybeUsdValue = FiatValueEstimation.UsdValue account.Currency

                let balanceInUsd =
                    match maybeUsdValue with
                    | None -> " (fiat price server unreachable... off-line?)"
                    | Some(usdValue) ->
                        sprintf "~ %s USD" ((balance * usdValue).ToString())

                let status = sprintf "Balance=[%s] %s"
                                 (balance.ToString())
                                 (balanceInUsd)
                Console.WriteLine(status)

            Console.WriteLine()
    else
        Console.WriteLine("No accounts have been created so far.")
    accounts.Count()

let rec AskAccount(): Account =
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
let rec AskDestination() =
    Console.Write("Destination address: ")
    let destAddress = Console.ReadLine()
    if not (destAddress.StartsWith("0x")) then
        Console.Error.WriteLine("Error: destination address should start with '0x', please try again.")
        AskDestination()
    else if (destAddress.Length <> ETHEREUM_ADDRESSES_LENGTH) then
        Console.Error.WriteLine(
            sprintf "Error: destination address should have a length of %d characters, please try again."
                ETHEREUM_ADDRESSES_LENGTH)
        AskDestination()
    else
        destAddress

let rec AskAmount() =
    Console.Write("Amount of ether: ")
    let amount = Console.ReadLine()
    match Decimal.TryParse(amount) with
    | (false, _) ->
        Console.Error.WriteLine("Error: please enter a numeric amount")
        AskAmount()
    | (true, parsedAdmount) ->
        parsedAdmount

let rec AskFee(currency: Currency): Option<EtherMinerFee> =
    let estimatedFee = AccountApi.EstimateFee(currency)
    let estimatedFeeInUsd =
        match FiatValueEstimation.UsdValue(currency) with
        | Some(usdValue) -> sprintf "(~%s USD)" ((usdValue * estimatedFee.EtherPriceForNormalTransaction).ToString())
        | None -> "(USD exchange rate unreachable... offline?)"
    Console.Write(sprintf "Estimated fee for this transaction would be:%s %s Ether %s %s Do you accept? (Y/N): "
                      Environment.NewLine
                      (estimatedFee.EtherPriceForNormalTransaction.ToString())
                      estimatedFeeInUsd
                      Environment.NewLine
                 )
    let yesNoAnswer = Console.ReadLine().ToLowerInvariant()
    if (yesNoAnswer = "y") then
        Some(estimatedFee)
    else if (yesNoAnswer = "n") then
        None
    else
        AskFee(currency)

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

let rec PerformOptions(numAccounts: int) =
    match AskOption(numAccounts) with
    | Options.Exit -> exit 0
    | Options.CreateAccount ->
        let currency = AskCurrency()
        let password = AskPassword true
        let account = AccountApi.Create currency password
        Console.WriteLine("Account created: " + account.PublicAddress)
    | Options.Refresh -> ()
    | Options.SendPayment ->
        let account = AskAccount()
        let destination = AskDestination()
        let amount = AskAmount()
        let maybeFee = AskFee(account.Currency)
        match maybeFee with
        | None -> ()
        | Some(fee) -> TrySendAmount account destination amount fee
    | _ -> failwith "Unreachable"

let rec ProgramMainLoop() =
    let numAccounts = DisplayStatus()
    PerformOptions(numAccounts)
    ProgramMainLoop()

[<EntryPoint>]
let main argv =
    ProgramMainLoop()

    0 // return an integer exit code
