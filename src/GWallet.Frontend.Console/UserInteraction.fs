namespace GWallet.Frontend.Console

open System
open System.IO
open System.Linq
open System.Globalization

open GWallet.Backend

type internal Options =
    | Exit               = 0
    | Refresh            = 1
    | CreateAccounts     = 2
    | SendPayment        = 3
    | AddReadonlyAccounts= 4
    | SignOffPayment     = 5
    | BroadcastPayment   = 6
    | ArchiveAccount     = 7
    | PairToWatchWallet  = 8

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

    let rec FindMatchingOption<'T> (optIntroduced, allOptions: List<'T*int>): 'T =
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
        let noAccounts = numAccounts = 0
        match option with
        | Options.SendPayment
        | Options.SignOffPayment
        | Options.ArchiveAccount
        | Options.PairToWatchWallet
            ->
                not noAccounts
        | Options.CreateAccounts -> noAccounts
        | _ -> true

    let rec internal AskFileNameToLoad (askText: string): FileInfo =
        Console.Write askText

        let fileName = Console.ReadLine()

        let file = FileInfo(fileName)
        if (file.Exists) then
            file
        else
            Presentation.Error "File not found, try again."
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

    let rec private AskDob(): DateTime =
        let format = "dd/MM/yyyy"
        Console.Write(sprintf "Write your date of birth (format '%s'): " format)
        let dob = Console.ReadLine()
        match (DateTime.TryParseExact(dob, format, CultureInfo.InvariantCulture, DateTimeStyles.None)) with
        | false,_ ->
            Presentation.Error "Incorrect date or date format, please try again."
            AskDob()
        | true,parsedDateTime ->
            parsedDateTime

    let rec private AskEmail(): string =
        Console.Write("Write your e-mail address (that you'll never forget): ")
        let email = Console.ReadLine()
        Console.Write("Repeat it: ")
        let email2 = Console.ReadLine()
        if (email <> email2) then
            Presentation.Error "E-mail addresses are not the same, please try again."
            AskEmail()
        else
            email

    let rec AskBrainSeed(): string*DateTime*string =
        Console.WriteLine()

        Console.Write("Write a seed passphrase (for disaster recovery) for your new wallet: ")
        let passphrase = ConsoleReadPasswordLine()

        Console.Write("Repeat the seed passphrase: ")
        let passphrase2 = ConsoleReadPasswordLine()
        if (passphrase <> passphrase2) then
            Presentation.Error "Passphrases are not the same, please try again."
            AskBrainSeed ()
        else
            let dob = AskDob()
            let email = AskEmail()
            passphrase,dob,email


    let rec AskPassword(repeat: bool): string =
        Console.WriteLine()

        Console.Write("Write the password to unlock your account (when making payments) in this device: ")
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

    let private BalanceInUsdString balance maybeUsdValue =
        match maybeUsdValue with
        | NotFresh(NotAvailable) -> Presentation.ExchangeRateUnreachableMsg
        | Fresh(usdValue) ->
            sprintf "~ %s USD" (balance * usdValue |> Formatting.DecimalAmount CurrencyType.Fiat)
        | NotFresh(Cached(usdValue,time)) ->
            sprintf "~ %s USD (last known rate as of %s)"
                (balance * usdValue |> Formatting.DecimalAmount CurrencyType.Fiat)
                (time |> Presentation.ShowSaneDate)

    let DisplayAccountStatus accountNumber (account: IAccount) (maybeBalance: MaybeCached<decimal>): unit =
        let maybeReadOnly =
            match account with
            | :? ReadOnlyAccount -> "(READ-ONLY)"
            | _ -> String.Empty

        let accountInfo = sprintf "Account %d: %s%sCurrency=[%A] Address=[%s]"
                                accountNumber maybeReadOnly Environment.NewLine
                                account.Currency
                                account.PublicAddress
        Console.WriteLine(accountInfo)

        let maybeUsdValue = FiatValueEstimation.UsdValue account.Currency

        match maybeBalance with
        | NotFresh(NotAvailable) ->
            Console.WriteLine("Unknown balance (Network unreachable... off-line?)")
        | NotFresh(Cached(balance,time)) ->
            let status = sprintf "Last known balance=[%s] (as of %s) %s %s"
                                (balance |> Formatting.DecimalAmount CurrencyType.Crypto)
                                (time |> Presentation.ShowSaneDate)
                                Environment.NewLine
                                (BalanceInUsdString balance maybeUsdValue)
            Console.WriteLine(status)
        | Fresh(balance) ->
            let status = sprintf "Balance=[%s] %s"
                                (balance |> Formatting.DecimalAmount CurrencyType.Crypto)
                                (BalanceInUsdString balance maybeUsdValue)
            Console.WriteLine(status)

        Console.WriteLine (sprintf "History -> %s" ((BlockExplorer.GetTransactionHistory account).ToString()))

    let private GetAccountBalances (accounts: seq<IAccount>): Async<array<IAccount*MaybeCached<decimal>>> =
        let getAccountBalance(account: IAccount): Async<IAccount*MaybeCached<decimal>> =
            async {
                // The console frontend cannot really take much advantage of the Fast|Analysis distinction here (as
                // opposed to the other frontends) because it doesn't have automatic balance refresh (it's this
                // operation the one that should only use Analysis mode). If we used Mode.Fast here, then the console
                // frontend would never re-discover slow/failing servers or even ones with no history
                let mode = Mode.Analysis

                let! balance = Account.GetShowableBalance account mode None
                return (account,balance)
            }
        let accountAndBalancesToBeQueried = accounts |> Seq.map getAccountBalance
        Async.Parallel accountAndBalancesToBeQueried

    let DisplayAccountStatuses(whichAccount: WhichAccount) =
        let rec displayAllAndSumBalance (accounts: seq<IAccount*MaybeCached<decimal>>)
                                         currentIndex
                                        (currentSumMap: Map<Currency,Option<decimal>>)
                                        : Map<Currency,Option<decimal>> =
            let account,maybeBalance = accounts.ElementAt(currentIndex)
            DisplayAccountStatus (currentIndex+1) account maybeBalance
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
                                let cryptoValue = Formatting.DecimalAmount CurrencyType.Crypto onlineBalance
                                let total = sprintf "Total %A: %s (%s)" currency cryptoValue fiatValue
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
                let accountsWithBalances = GetAccountBalances accounts |> Async.RunSynchronously
                let currencyTotals = displayAllAndSumBalance accountsWithBalances 0 Map.empty

                let maybeTotalInUsd = displayTotalAndSumFiatBalance currencyTotals
                match maybeTotalInUsd with
                | None -> ()
                | Some(totalInUsd) ->
                    Console.WriteLine()
                    Console.WriteLine(sprintf "Total estimated value in USD: %s"
                                          (Formatting.DecimalAmount CurrencyType.Fiat totalInUsd))
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
                failwithf "account %s(%A) not found in config, or more than one with same public address?"
                          account.PublicAddress account.Currency
            for i = 0 to allAccounts.Count() - 1 do
                let iterAccount = allAccounts.ElementAt(i)
                if (matchFilter (iterAccount)) then
                    DisplayAccountStatus (i+1) iterAccount |> ignore

    let rec AskYesNo (question: string): bool =
        Console.Write (sprintf "%s (Y/N): " question)
        let yesNoAnswer = Console.ReadLine().ToLowerInvariant()
        if (yesNoAnswer = "y") then
            true
        elif (yesNoAnswer = "n") then
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
            | AddressMissingProperPrefix(possiblePrefixes) ->
                let possiblePrefixesStr = String.Join(", ", possiblePrefixes)
                Presentation.Error (sprintf "Address starts with the wrong prefix. Valid prefixes: %s"
                                        possiblePrefixesStr)
                AskPublicAddress currency askText

            | AddressWithInvalidLength lengthInfo ->
                match lengthInfo.Count() with
                | 1 ->
                    let lengthLimitViolated = lengthInfo.ElementAt 0
                    if publicAddress.Length <> lengthLimitViolated then
                        Presentation.Error
                            (sprintf "Address should have a length of %d characters, please try again."
                                lengthLimitViolated)
                    else
                        failwithf "Address introduced '%s' gave a length error with a limit that matches its length: %d=%d. Report this bug."
                                  publicAddress lengthLimitViolated publicAddress.Length
                | 2 ->
                    let minLength,maxLength = lengthInfo.ElementAt 0,lengthInfo.ElementAt 1
                    if publicAddress.Length < minLength then
                        Presentation.Error
                            (sprintf "Address should have a length not lower than %d characters, please try again."
                                minLength)
                    elif publicAddress.Length > maxLength then
                        Presentation.Error
                            (sprintf "Address should have a length not higher than %d characters, please try again."
                                maxLength)
                    else
                        Presentation.Error
                            (sprintf "Address should have a length of either %d or %d characters, please try again."
                                minLength maxLength)
                | _ ->
                    failwithf "AddressWithInvalidLength returned an invalid parameter length (%d). Report this bug."
                              (lengthInfo.Count())

                AskPublicAddress currency askText
            | AddressWithInvalidChecksum maybeAddressWithValidChecksum ->
                Console.Error.WriteLine "WARNING: the address provided didn't pass the checksum, are you sure you copied it properly?"
                Console.Error.WriteLine "(If you copied it by hand or somebody dictated it to you, you probably made a spelling mistake.)"
                match maybeAddressWithValidChecksum with
                | None ->
                    AskPublicAddress currency askText
                | Some addressWithValidChecksum ->
                    Console.Error.WriteLine "(If you used the clipboard, you're likely copying it from a service that doesn't have checksum validation.)"
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

    let rec AskParticularAmount() =
        Console.Write("Amount: ")
        let amount = Console.ReadLine()
        match Decimal.TryParse(amount) with
        | (false, _) ->
            Presentation.Error "Please enter a numeric amount."
            AskParticularAmount()
        | true, parsedAmount ->
            if not (parsedAmount > 0m) then
                Presentation.Error "Please enter a positive amount."
                AskParticularAmount()
            else
                parsedAmount

    let rec AskParticularUsdAmount currency usdValue (maybeTime:Option<DateTime>): Option<decimal> =
        let usdAmount = AskParticularAmount()
        let exchangeRateDateMsg =
            match maybeTime with
            | None -> String.Empty
            | Some(time) -> sprintf " (as of %s)" (Presentation.ShowSaneDate time)
        let exchangeMsg = sprintf "%s USD per %A%s" (usdValue.ToString())
                                                    currency
                                                    exchangeRateDateMsg
        let etherAmount = usdAmount / usdValue
        Console.WriteLine(sprintf "At an exchange rate of %s, %A amount would be:%s%s"
                              exchangeMsg currency
                              Environment.NewLine
                              (Formatting.DecimalAmount CurrencyType.Crypto etherAmount))
        if AskYesNo "Do you accept?" then
            Some(usdAmount)
        else
            None

    let private AskParticularFiatAmountWithRate cryptoCurrency usdValue time: Option<decimal> =
        match AskParticularUsdAmount cryptoCurrency usdValue time with
        | None -> None
        | Some(usdAmount) -> Some(usdAmount / usdValue)

    exception InsufficientBalance
    let rec internal AskAmount (account: IAccount): Option<TransferAmount> =
        let rec AskParticularAmountOption currentBalance (amountOption: AmountOption): Option<TransferAmount> =
            try
                match amountOption with
                | AmountOption.AllBalance ->
                    TransferAmount(currentBalance, currentBalance, account.Currency) |> Some
                | AmountOption.CertainCryptoAmount ->
                    let specificCryptoAmount = AskParticularAmount()
                    if (specificCryptoAmount > currentBalance) then
                        raise InsufficientBalance
                    TransferAmount(specificCryptoAmount, currentBalance, account.Currency) |> Some
                | AmountOption.ApproxEquivalentFiatAmount ->
                    match FiatValueEstimation.UsdValue account.Currency with
                    | NotFresh(NotAvailable) ->
                        Presentation.Error "USD exchange rate unreachable (offline?), please choose a different option."
                        AskAmount account
                    | Fresh usdValue ->
                        let maybeCryptoAmount = AskParticularFiatAmountWithRate account.Currency usdValue None
                        match maybeCryptoAmount with
                        | None -> None
                        | Some cryptoAmount ->
                            if (cryptoAmount > currentBalance) then
                                raise InsufficientBalance
                            TransferAmount(cryptoAmount, currentBalance, account.Currency) |> Some
                    | NotFresh(Cached(usdValue,time)) ->
                        let maybeCryptoAmount = AskParticularFiatAmountWithRate account.Currency usdValue (Some(time))
                        match maybeCryptoAmount with
                        | None -> None
                        | Some cryptoAmount ->
                            if (cryptoAmount > currentBalance) then
                                raise InsufficientBalance
                            TransferAmount(cryptoAmount, currentBalance, account.Currency) |> Some
            with
            | :? InsufficientBalance ->
                Presentation.Error "Amount surpasses current balance, try again."
                AskParticularAmountOption currentBalance amountOption

        let showableBalance =
            Account.GetShowableBalance account Mode.Fast None
                |> Async.RunSynchronously

        match showableBalance with
        | NotFresh(NotAvailable) ->
            Presentation.Error "Balance not available if offline."
            None

        | Fresh(balance) | NotFresh(Cached(balance,_)) ->

            if not (balance > 0m) then
                // TODO: maybe we should check the balance before asking the destination address
                Presentation.Error "Account needs to have positive balance."
                None
            else
                Console.WriteLine "There are various options to specify the amount of your transaction:"
                Console.WriteLine(sprintf "1. Exact amount in %A" account.Currency)
                Console.WriteLine "2. Approximate amount in USD"
                Console.WriteLine(sprintf "3. All balance existing in the account (%g %A)"
                                          balance account.Currency)

                let amountOption = AskAmountOption()
                AskParticularAmountOption balance amountOption

    let AskFee account amount destination: Option<IBlockchainFeeInfo> =
        try
            let txMetadataWithFeeEstimation =
                Account.EstimateFee account amount destination |> Async.RunSynchronously
            Presentation.ShowFee amount.Currency txMetadataWithFeeEstimation
            let accept = AskYesNo "Do you accept?"
            if accept then
                Some(txMetadataWithFeeEstimation)
            else
                None
        with
        | InsufficientBalanceForFee feeValue ->
            // TODO: show fiat value in this error msg below?
            Presentation.Error (
                sprintf
                    "Estimated fee is too high (%M) for the remaining balance, use a different account or a different amount."
                    feeValue
            )
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
