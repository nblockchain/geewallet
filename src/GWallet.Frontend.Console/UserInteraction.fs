namespace GWallet.Frontend.Console

open System
open System.Linq

open GWallet.Backend

type Options =
    | Exit               = 0
    | Refresh            = 1
    | CreateAccount      = 2
    | SendPayment        = 3
    | AddReadonlyAccount = 4
    | SignOffPayment     = 5
    | BroadcastPayment   = 6

type WhichAccount =
    All of seq<IAccount> | MatchingWith of IAccount

module UserInteraction =

    let PressAnyKeyToContinue() =
        Console.WriteLine ()
        Console.Write "Press any key to continue..."
        Console.Read () |> ignore
        Console.WriteLine ()

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
                                              (Presentation.ConvertPascalCaseToSentence (option.ToString())))
                        yield option, int option
            } |> List.ofSeq
        Console.Write("Choose option to perform: ")
        let optIntroduced = System.Console.ReadLine()
        try
            FindMatchingOption(optIntroduced, allOptionsAvailable)
        with
        | :? NoOptionFound -> AskOption(numAccounts)

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
                Presentation.Error "Passwords are not the same, please try again."
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
            | NotFresh(NotAvailable) -> Presentation.ExchangeRateUnreachableMsg
            | Fresh(usdValue) ->
                sprintf "~ %s USD" (balance * usdValue |> Presentation.ShowDecimalForHumans CurrencyType.Fiat)
            | NotFresh(Cached(usdValue,time)) ->
                sprintf "~ %s USD (last known rate as of %s)"
                    (balance * usdValue |> Presentation.ShowDecimalForHumans CurrencyType.Fiat)
                    (time |> Presentation.ShowSaneDate)

        let maybeUsdValue = FiatValueEstimation.UsdValue account.Currency

        let maybeBalance = Account.GetBalance(account)
        match maybeBalance with
        | NotFresh(NotAvailable) ->
            Console.WriteLine("Unknown balance (Network unreachable... off-line?)")
        | NotFresh(Cached(balance,time)) ->
            let status = sprintf "Last known balance=[%s] (as of %s) %s %s"
                                (balance |> Presentation.ShowDecimalForHumans CurrencyType.Crypto)
                                (time |> Presentation.ShowSaneDate)
                                Environment.NewLine
                                (balanceInUsdString balance maybeUsdValue)
            Console.WriteLine(status)
        | Fresh(balance) ->
            let status = sprintf "Balance=[%s] %s"
                                (balance |> Presentation.ShowDecimalForHumans CurrencyType.Crypto)
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
            let allAccounts =  Account.GetAllAccounts()
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

    let private ETHEREUM_ADDRESSES_LENGTH = 42
    let rec AskPublicAddress (askText: string) =
        Console.Write askText
        let publicAddress = Console.ReadLine()
        if not (publicAddress.StartsWith("0x")) then
            Presentation.Error "Address should start with '0x', please try again."
            AskPublicAddress askText
        else if (publicAddress.Length <> ETHEREUM_ADDRESSES_LENGTH) then
            Presentation.Error
                (sprintf "Address should have a length of %d characters, please try again."
                    ETHEREUM_ADDRESSES_LENGTH)
            AskPublicAddress askText
        else
            publicAddress

    type private AmountOption =
        | AllBalance
        | CertainCryptoAmount
        | ApproxEquivalentFiatAmount

    let rec private AskAmountOption(): AmountOption =
        Console.Write("Choose an option from the above: ")
        let optIntroduced = System.Console.ReadLine()
        match Int32.TryParse(optIntroduced) with
        | false, _ -> AskAmountOption()
        | true, optionParsed ->
            match optionParsed with
            | 1 -> AmountOption.CertainCryptoAmount
            | 2 -> AmountOption.ApproxEquivalentFiatAmount
            | 3 -> AmountOption.AllBalance
            | _ -> AskAmountOption()

    type internal AmountToTransfer =
        | AllBalance of decimal
        | CertainCryptoAmount of decimal
        | CancelOperation

    let rec AskParticularAmount() =
        Console.Write("Amount: ")
        let amount = Console.ReadLine()
        match Decimal.TryParse(amount) with
        | (false, _) ->
            Presentation.Error "Please enter a numeric amount."
            AskParticularAmount()
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

    let rec AskParticularUsdAmount usdValue (maybeTime:Option<DateTime>): Option<decimal> =
        let usdAmount = AskParticularAmount()
        let exchangeRateDateMsg =
            match maybeTime with
            | None -> String.Empty
            | Some(time) -> sprintf " (as of %s)" (Presentation.ShowSaneDate time)
        let exchangeMsg = sprintf "%s USD per Ether%s" (usdValue.ToString()) exchangeRateDateMsg
        let etherAmount = usdAmount / usdValue
        Console.WriteLine(sprintf "At an exchange rate of %s, Ether amount would be:%s%s"
                              exchangeMsg Environment.NewLine (etherAmount.ToString()))
        if AskAccept() then
            Some(usdAmount)
        else
            None

    let private GetCryptoAmount usdValue time =
        match AskParticularUsdAmount usdValue time with
        | None -> AmountToTransfer.CancelOperation
        | Some(usdAmount) ->
            let ethAmount = usdAmount / usdValue
            AmountToTransfer.CertainCryptoAmount(ethAmount)

    let rec internal AskAmount account: AmountToTransfer =
        Console.WriteLine("There are various options to specify the amount of your transaction:")
        Console.WriteLine("1. Exact amount in Ether")
        Console.WriteLine("2. Approximate amount in USD")
        Console.WriteLine("3. All balance existing in the account")
        match AskAmountOption() with
        | AmountOption.AllBalance ->
            match Account.GetBalance(account) with
            | NotFresh(NotAvailable) ->
                Presentation.Error "Balance not available if offline."
                AmountToTransfer.CancelOperation
            | Fresh(amount) ->
                AmountToTransfer.AllBalance(amount)
            | NotFresh(Cached(amount,_)) ->
                AmountToTransfer.AllBalance(amount)
        | AmountOption.CertainCryptoAmount ->
            AmountToTransfer.CertainCryptoAmount(AskParticularAmount())
        | AmountOption.ApproxEquivalentFiatAmount ->
            match FiatValueEstimation.UsdValue account.Currency with
            | NotFresh(NotAvailable) ->
                Presentation.Error "USD exchange rate unreachable (offline?), please choose a different option."
                AskAmount account
            | Fresh(usdValue) ->
                GetCryptoAmount usdValue None
            | NotFresh(Cached(usdValue,time)) ->
                GetCryptoAmount usdValue (Some(time))

    let AskFee(currency: Currency): Option<EtherMinerFee> =
        let estimatedFee = Account.EstimateFee(currency)
        Presentation.ShowFee currency estimatedFee
        let accept = AskAccept()
        if accept then
            Some(estimatedFee)
        else
            None

    let rec AskAccount(): IAccount =
        let allAccounts = Account.GetAllAccounts()
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
