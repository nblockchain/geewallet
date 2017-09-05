namespace GWallet.Frontend.Console

open System
open System.IO
open System.Linq

open GWallet.Backend

type internal Options =
    | Exit               = 0
    | Refresh            = 1
    | CreateAccount      = 2
    | SendPayment        = 3
    | AddReadonlyAccount = 4
    | SignOffPayment     = 5
    | BroadcastPayment   = 6
    | ArchiveAccount     = 7

type WhichAccount =
    All of seq<IAccount> | MatchingWith of IAccount

module UserInteraction =

    let PressAnyKeyToContinue() =
        Console.WriteLine ()
        Console.Write "Press any key to continue..."
        Console.ReadKey true |> ignore
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

    let internal OptionAvailable (option: Options) (numAccounts: int) =
        let anyAccount = numAccounts > 0
        match option with
        | Options.SendPayment -> anyAccount
        | Options.ArchiveAccount -> anyAccount
        | _ -> true

    let rec internal AskFileNameToLoad (askText: string): FileInfo =
        Console.Write askText

        let fileName = Console.ReadLine()

        let file = FileInfo(fileName)
        if (file.Exists) then
            file
        else
            Console.Error.WriteLine "File not found, try again."
            AskFileNameToLoad askText

    let rec internal AskOption(numAccounts: int): Options =
        Console.WriteLine("Available options:")

        // TODO: move these 2 lines below to FSharpUtil?
        let allOptions = Enum.GetValues(typeof<Options>).Cast<Options>() |> List.ofSeq

        let allOptionsAvailable =
            seq {
                for option in allOptions do
                    if OptionAvailable option numAccounts then
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

        let allCurrencies = Currency.GetAll()

        for i = 0 to (allCurrencies.Count() - 1) do
            Console.WriteLine(sprintf "%d: %s" (i+1) (allCurrencies.ElementAt(i).ToString()))
        Console.Write("Select currency: ")
        let optIntroduced = System.Console.ReadLine()
        match Int32.TryParse(optIntroduced) with
        | false, _ -> AskCurrency()
        | true, optionParsed ->
            if (optionParsed < 1 || optionParsed > allCurrencies.Count()) then
                AskCurrency()
            else
                allCurrencies.ElementAt(optionParsed - 1)

    let private BalanceInUsdString balance maybeUsdValue =
        match maybeUsdValue with
        | NotFresh(NotAvailable) -> Presentation.ExchangeRateUnreachableMsg
        | Fresh(usdValue) ->
            sprintf "~ %s USD" (balance * usdValue |> Presentation.ShowDecimalForHumans CurrencyType.Fiat)
        | NotFresh(Cached(usdValue,time)) ->
            sprintf "~ %s USD (last known rate as of %s)"
                (balance * usdValue |> Presentation.ShowDecimalForHumans CurrencyType.Fiat)
                (time |> Presentation.ShowSaneDate)

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
                                (BalanceInUsdString balance maybeUsdValue)
            Console.WriteLine(status)
        | Fresh(balance) ->
            let status = sprintf "Balance=[%s] %s"
                                (balance |> Presentation.ShowDecimalForHumans CurrencyType.Crypto)
                                (BalanceInUsdString balance maybeUsdValue)
            Console.WriteLine(status)

        maybeBalance

    let DisplayAccountStatuses(whichAccount: WhichAccount) =
        let rec displayAllAndSumBalance (accounts: seq<IAccount>)
                                         currentIndex
                                        (currentSumMap: Map<Currency,Option<decimal>>)
                                        : Map<Currency,Option<decimal>> =
            let account = accounts.ElementAt(currentIndex)
            let maybeBalance = DisplayAccountStatus (currentIndex+1) account
            Console.WriteLine ()

            let balanceToSum: Option<decimal> =
                match maybeBalance with
                | Fresh(balance) -> Some(balance)
                | NotFresh(Cached(balance,_)) -> Some(balance)
                | _ -> None

            let newBalanceForCurrency: Option<decimal> =
                match balanceToSum with
                | None -> None
                | Some(thisBalance) ->
                    match Map.tryFind account.Currency currentSumMap with
                    | None ->
                        Some(thisBalance)
                    | Some(None) ->
                        // there was a previous error, so we want to keep the total balance as N/A
                        None
                    | Some(Some(sumSoFar)) ->
                        Some(sumSoFar+thisBalance)

            let maybeCleanedUpMapForReplacement =
                match Map.containsKey account.Currency currentSumMap with
                | false ->
                    currentSumMap
                | true ->
                    Map.remove account.Currency currentSumMap

            let newAcc = Map.add account.Currency newBalanceForCurrency maybeCleanedUpMapForReplacement

            if (currentIndex < accounts.Count() - 1) then
                displayAllAndSumBalance accounts (currentIndex + 1) newAcc
            else
                newAcc

        let rec displayTotalAndSumFiatBalance (currenciesToBalances: Map<Currency,Option<decimal>>): Option<decimal> =
            let usdTotals =
                seq {
                    for KeyValue(currency, balance) in currenciesToBalances do
                        match balance with
                        | None -> ()
                        | Some(onlineBalance) ->
                            let maybeUsdValue = FiatValueEstimation.UsdValue currency
                            match maybeUsdValue with
                            | NotFresh(NotAvailable) -> yield None
                            | Fresh(usdValue) | NotFresh(Cached(usdValue,_)) ->
                                let fiatValue = BalanceInUsdString onlineBalance maybeUsdValue
                                let total = sprintf "Total %s: %s (%s)" (currency.ToString()) (onlineBalance.ToString()) fiatValue
                                yield Some(onlineBalance * usdValue)
                                Console.WriteLine (total)
                } |> List.ofSeq
            if (usdTotals.Any(fun maybeUsdTotal -> maybeUsdTotal.IsNone)) then
                None
            else
                Some(usdTotals.Sum(fun maybeUsdTotal -> maybeUsdTotal.Value))

        match whichAccount with
        | WhichAccount.All(accounts) ->
            Console.WriteLine ()
            Console.WriteLine "*** STATUS ***"

            if (accounts.Any()) then
                let currencyTotals = displayAllAndSumBalance accounts 0 Map.empty

                let maybeTotalInUsd = displayTotalAndSumFiatBalance currencyTotals
                match maybeTotalInUsd with
                | None -> ()
                | Some(totalInUsd) ->
                    Console.WriteLine()
                    Console.WriteLine("Total estimated value in USD: " +
                        Presentation.ShowDecimalForHumans CurrencyType.Fiat totalInUsd)
            else
                Console.WriteLine("No accounts have been created so far.")
            Console.WriteLine()

        | MatchingWith(account) ->
            let allAccounts =  Account.GetAllActiveAccounts()
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
                    DisplayAccountStatus (i+1) iterAccount |> ignore

    let rec AskYesNo (question: string): bool =
        Console.Write (sprintf "%s (Y/N): " question)
        let yesNoAnswer = Console.ReadLine().ToLowerInvariant()
        if (yesNoAnswer = "y") then
            true
        else if (yesNoAnswer = "n") then
            false
        else
            AskYesNo question

    let rec AskPublicAddress currency (askText: string): string =
        Console.Write askText
        let publicAddress = Console.ReadLine()
        let validatedAddress =
            try
                Account.ValidateAddress currency publicAddress
                publicAddress
            with
            | AddressMissingProperPrefix(prefix) ->
                Presentation.Error (sprintf "Address should start with '%s' prefix, please try again." prefix)
                AskPublicAddress currency askText
            | AddressWithInvalidLength(requiredLength) ->
                Presentation.Error
                    (sprintf "Address should have a length of %d characters, please try again."
                        requiredLength)
                AskPublicAddress currency askText
            | AddressWithInvalidChecksum(addressWithValidChecksum) ->
                Console.Error.WriteLine "WARNING: the address provided didn't pass the checksum, are you sure you copied it properly?"
                Console.Error.WriteLine "(If you used the clipboard, you're likely copying it from a service that doesn't have checksum validation.)"
                Console.Error.WriteLine "(If you copied it by hand or somebody dictated it to you, you probably made a spelling mistake.)"
                let continueWithoutChecksum = AskYesNo "Continue with this address?"
                if (continueWithoutChecksum) then
                    addressWithValidChecksum
                else
                    AskPublicAddress currency askText
        validatedAddress

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

    type internal AmountDesire =
        | AllBalance
        | CertainCryptoAmount

    type internal AmountToTransfer =
        {
            Value: decimal;
            Type: AmountDesire;
        }

    let rec AskParticularAmount() =
        Console.Write("Amount: ")
        let amount = Console.ReadLine()
        match Decimal.TryParse(amount) with
        | (false, _) ->
            Presentation.Error "Please enter a numeric amount."
            AskParticularAmount()
        | (true, parsedAdmount) ->
            parsedAdmount

    let rec AskParticularUsdAmount currency usdValue (maybeTime:Option<DateTime>): Option<decimal> =
        let usdAmount = AskParticularAmount()
        let exchangeRateDateMsg =
            match maybeTime with
            | None -> String.Empty
            | Some(time) -> sprintf " (as of %s)" (Presentation.ShowSaneDate time)
        let exchangeMsg = sprintf "%s USD per %s%s" (usdValue.ToString())
                                                    (currency.ToString())
                                                    exchangeRateDateMsg
        let etherAmount = usdAmount / usdValue
        Console.WriteLine(sprintf "At an exchange rate of %s, %s amount would be:%s%s"
                              exchangeMsg (currency.ToString())
                              Environment.NewLine (etherAmount.ToString()))
        if AskYesNo "Do you accept?" then
            Some(usdAmount)
        else
            None

    let private GetCryptoAmount cryptoCurrency usdValue time =
        match AskParticularUsdAmount cryptoCurrency usdValue time with
        | None -> None
        | Some(usdAmount) ->
            let ethAmount = usdAmount / usdValue
            Some({ Value = ethAmount; Type = CertainCryptoAmount; })

    let rec internal AskAmount account: Option<AmountToTransfer> =
        Console.WriteLine("There are various options to specify the amount of your transaction:")
        Console.WriteLine(sprintf "1. Exact amount in %s" ((account:>IAccount).Currency.ToString()))
        Console.WriteLine("2. Approximate amount in USD")
        Console.WriteLine("3. All balance existing in the account")
        match AskAmountOption() with
        | AmountOption.AllBalance ->
            match Account.GetBalance(account) with
            | NotFresh(NotAvailable) ->
                Presentation.Error "Balance not available if offline."
                None
            | Fresh(amount) | NotFresh(Cached(amount,_)) ->
                Some({ Value = amount; Type = AllBalance; })
        | AmountOption.CertainCryptoAmount ->
            Some({ Value = AskParticularAmount(); Type = CertainCryptoAmount; })
        | AmountOption.ApproxEquivalentFiatAmount ->
            match FiatValueEstimation.UsdValue account.Currency with
            | NotFresh(NotAvailable) ->
                Presentation.Error "USD exchange rate unreachable (offline?), please choose a different option."
                AskAmount account
            | Fresh(usdValue) ->
                GetCryptoAmount account.Currency usdValue None
            | NotFresh(Cached(usdValue,time)) ->
                GetCryptoAmount account.Currency usdValue (Some(time))

    let AskFee account amount: Option<IBlockchainFee> =
        let estimatedFee = Account.EstimateFee account amount
        Presentation.ShowFee account.Currency estimatedFee
        let accept = AskYesNo "Do you accept?"
        if accept then
            Some(estimatedFee)
        else
            None

    let rec AskAccount(): IAccount =
        let allAccounts = Account.GetAllActiveAccounts()
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
