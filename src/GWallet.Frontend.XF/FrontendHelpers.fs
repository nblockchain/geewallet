namespace GWallet.Frontend.XF

open System
open System.Threading.Tasks

open Xamarin.Forms
open ZXing
open ZXing.Mobile

open GWallet.Backend

module FrontendHelpers =

    type IGlobalAppState =
        abstract member Awake: bool with get

        [<CLIEvent>]
        abstract member Resumed: IEvent<unit> with get
        [<CLIEvent>]
        abstract member GoneToSleep: IEvent<unit> with get

    type IAugmentablePayPage =
        abstract member AddTransactionScanner: unit -> unit

    let internal BigFontSize = 22.

    let internal MediumFontSize = 20.

    let internal MagicGtkNumber = 20.

    let private defaultFiatCurrency = "USD"

    let internal ExchangeRateUnreachableMsg = sprintf " (~ ? %s)" defaultFiatCurrency

    //FIXME: right now the UI doesn't explain what the below element means when it shows it, we should add a legend...
    let internal ExchangeOutdatedVisualElement = "*"

    // these days cryptos are not so volatile, so 30mins should be good...
    let internal TimeSpanToConsiderExchangeRateOutdated = TimeSpan.FromMinutes 30.0

    let MaybeReturnOutdatedMarkForOldDate (date: DateTime) =
        if (date + TimeSpanToConsiderExchangeRateOutdated < DateTime.Now) then
            ExchangeOutdatedVisualElement
        else
            String.Empty

    // FIXME: share code between Frontend.Console and Frontend.XF
    let BalanceInUsdString (balance: decimal) (maybeUsdValue: MaybeCached<decimal>)
                               : MaybeCached<decimal>*string =
        match maybeUsdValue with
        | NotFresh(NotAvailable) ->
            NotFresh(NotAvailable),ExchangeRateUnreachableMsg
        | Fresh(usdValue) ->
            let fiatBalance = usdValue * balance
            Fresh(fiatBalance),sprintf "~ %s %s"
                                   (Formatting.DecimalAmount CurrencyType.Fiat fiatBalance)
                                   defaultFiatCurrency
        | NotFresh(Cached(usdValue,time)) ->
            let fiatBalance = usdValue * balance
            NotFresh(Cached(fiatBalance,time)),sprintf "~ %s %s%s"
                                                    (Formatting.DecimalAmount CurrencyType.Fiat fiatBalance)
                                                    defaultFiatCurrency
                                                    (MaybeReturnOutdatedMarkForOldDate time)

    let UpdateBalance (balance:MaybeCached<decimal>) currency (balanceLabel: Label) (fiatBalanceLabel: Label)
                          : MaybeCached<decimal> =
        let maybeBalanceAmount =
            match balance with
            | NotFresh(NotAvailable) ->
                None
            | NotFresh(Cached(amount,_)) ->
                Some amount
            | Fresh(amount) ->
                Some amount
        let balanceAmountStr,fiatAmount,fiatAmountStr =
            match maybeBalanceAmount with
            | None ->
                sprintf "? %A" currency, NotFresh(NotAvailable), sprintf "? %s" defaultFiatCurrency
            | Some balanceAmount ->
                let cryptoAmount = Formatting.DecimalAmount CurrencyType.Crypto balanceAmount
                let cryptoAmountStr = sprintf "%s %A" cryptoAmount currency
                let usdRate = FiatValueEstimation.UsdValue currency
                let fiatAmount,fiatAmountStr = BalanceInUsdString balanceAmount usdRate
                cryptoAmountStr,fiatAmount,fiatAmountStr
        Device.BeginInvokeOnMainThread(fun _ ->
            balanceLabel.Text <- balanceAmountStr
            fiatBalanceLabel.Text <- fiatAmountStr
        )
        fiatAmount

    let UpdateBalanceAsync account (balanceLabel: Label) (fiatBalanceLabel: Label)
                               : Async<IAccount*Label*Label*MaybeCached<decimal>> =
        async {
            let! balance = Account.GetShowableBalance account
            let fiatAmount = UpdateBalance balance account.Currency balanceLabel fiatBalanceLabel
            return account,balanceLabel,fiatBalanceLabel,fiatAmount
        }

    let UpdateCachedBalancesAsync (accountsWithLabels: seq<IAccount*Label*Label>)
                                      : Async<array<IAccount*Label*Label*MaybeCached<decimal>>> =
        let rec updateBalanceAccumulator (accountsWithLabels: List<IAccount*Label*Label>)
                                         (acc: List<IAccount*Label*Label*MaybeCached<decimal>>) =
            match accountsWithLabels with
            | [] -> acc
            | (account,cryptoBalanceLabel,fiatBalanceLabel)::tail ->
                let cachedBalance = Caching.Instance.RetreiveLastCompoundBalance account.PublicAddress account.Currency
                match cachedBalance with
                | Cached _ ->
                    let fiatAmount =
                        UpdateBalance (NotFresh cachedBalance) account.Currency cryptoBalanceLabel fiatBalanceLabel
                    let newElem = account,cryptoBalanceLabel,fiatBalanceLabel,fiatAmount
                    updateBalanceAccumulator tail (newElem::acc)
                | _ ->
                    failwith "Retreiving cached balance of a readonlyAccount(cold storage) returned N/A; but addition of this account should have stored it properly..."

        async {
            return (updateBalanceAccumulator (accountsWithLabels |> List.ofSeq) []) |> List.rev |> Array.ofList
        }

    // FIXME: share code between Frontend.Console and Frontend.XF
    // with this we want to avoid the weird default US format of starting with the month, then day, then year... sigh
    let ShowSaneDate (date: DateTime): string =
        date.ToString("dd-MMM-yyyy")

    // FIXME: add this use case to Formatting module, and with a unit test
    let ShowDecimalForHumansWithMax (currencyType: CurrencyType) (amount: decimal) (maxAmount: decimal)
                                                  : string =
        let amountOfDecimalsToShow =
            match currencyType with
            | CurrencyType.Fiat -> 2
            | CurrencyType.Crypto -> 5
        // https://stackoverflow.com/a/25451689/544947
        let truncated = amount - (amount % (1m / decimal(amountOfDecimalsToShow * 10)))
        if (truncated > maxAmount) then
            failwithf "how can %s be higher than %s?" (truncated.ToString()) (maxAmount.ToString())

        Formatting.DecimalAmount currencyType truncated

    // when running Task<unit> or Task<T> where we want to ignore the T, we should still make sure there is no exception,
    // & if there is, bring it to the main thread to fail fast, report to Sentry, etc, otherwise it gets ignored
    let DoubleCheckCompletion<'T> (task: Task<'T>) =
        task.ContinueWith(fun (t: Task<'T>) ->
            if (t.Exception <> null) then
                Device.BeginInvokeOnMainThread(fun _ ->
                    raise(task.Exception)
                )
        ) |> ignore
    let DoubleCheckCompletionNonGeneric (task: Task) =
        task.ContinueWith(fun (t: Task) ->
            if (t.Exception <> null) then
                Device.BeginInvokeOnMainThread(fun _ ->
                    raise(task.Exception)
                )
        ) |> ignore

    let DoubleCheckCompletionAsync<'T> (work: Async<'T>): unit =
        async {
            try
                let! _ = work
                ()
            with
            | ex ->
                Device.BeginInvokeOnMainThread(fun _ ->
                    raise(ex)
                )
            return ()
        } |> Async.Start

    let SwitchToNewPageDiscardingCurrentOne (currentPage: Page) (newPage: Page): unit =
        Device.BeginInvokeOnMainThread(fun _ ->
            NavigationPage.SetHasNavigationBar(newPage, false)
            currentPage.Navigation.InsertPageBefore(newPage, currentPage)
            currentPage.Navigation.PopAsync() |> DoubleCheckCompletion
        )

    let ChangeTextAndChangeBack (button: Button) (newText: string) =
        let initialText = button.Text
        button.IsEnabled <- false
        button.Text <- newText
        Task.Run(fun _ ->
            Task.Delay(TimeSpan.FromSeconds(2.0)).Wait()
            Device.BeginInvokeOnMainThread(fun _ ->
                button.Text <- initialText
                button.IsEnabled <- true
            )
        ) |> DoubleCheckCompletionNonGeneric

    let private CreateWidgetsForAccount (): Label*Label =
        let accountBalanceLabel = Label(Text = "...",
                                        VerticalOptions = LayoutOptions.Center,
                                        HorizontalOptions = LayoutOptions.Start)
        let fiatBalanceLabel = Label(Text = "...",
                                     VerticalOptions = LayoutOptions.Center,
                                     HorizontalOptions = LayoutOptions.EndAndExpand)

        // workaround to small default fonts in GTK (compared to other toolkits) so FIXME: file bug about this
        let magicGtkNumber = MagicGtkNumber
        accountBalanceLabel.FontSize <- magicGtkNumber
        fiatBalanceLabel.FontSize <- magicGtkNumber

        if (Device.RuntimePlatform = Device.GTK) then
            // workaround about Labels not respecting VerticalOptions.Center in GTK so FIXME: file bug about this
            accountBalanceLabel.TranslationY <- magicGtkNumber
            fiatBalanceLabel.TranslationY <- magicGtkNumber
            // workaround about Labels not putting a decent default left margin in GTK so FIXME: file bug about this
            accountBalanceLabel.TranslationX <- magicGtkNumber
            fiatBalanceLabel.TranslationX <- magicGtkNumber

        accountBalanceLabel,fiatBalanceLabel

    let CreateWidgetsForAccounts(accounts: seq<IAccount>): List<IAccount*Label*Label> =
        seq {
            for account in accounts do
                let cryptoLabel,fiatLabel = CreateWidgetsForAccount ()
                yield account,cryptoLabel,fiatLabel
        } |> List.ofSeq

    let BarCodeScanningOptions = MobileBarcodeScanningOptions(
                                     TryHarder = Nullable<bool> true,
                                     // TODO: propose PR to ZXing.Net/ZXing.Net.Mobile to change this to IEnumerable<T>:
                                     PossibleFormats = System.Collections.Generic.List<BarcodeFormat>(
                                         [ BarcodeFormat.QR_CODE ]
                                     ),
                                     UseNativeScanning = true
                                 )
    let BigQrCodeWarning = "NOTE: the QR codes of transactions are larger than the ones for addresses, so try to focus the camera well and make the QR code fit the whole view (even if it gets out of the limits of the grayed area) to try to get a successful scan."

