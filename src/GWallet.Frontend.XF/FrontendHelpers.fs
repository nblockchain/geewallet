namespace GWallet.Frontend.XF

open System
open System.Threading
open System.Threading.Tasks

open Xamarin.Forms
open ZXing
open ZXing.Mobile

open GWallet.Backend

type BalanceWidgets =
    {
        CryptoLabel: Label
        FiatLabel: Label
        Frame: Frame
    }

type BalanceSet = {
    Account: IAccount;
    Widgets: BalanceWidgets
}

type BalanceState = {
    BalanceSet: BalanceSet;
    FiatAmount: MaybeCached<decimal>;
    ImminentIncomingPayment: Option<bool>;
}

module FrontendHelpers =

    let private enableGtkWorkarounds = true

    type IGlobalAppState =
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
        if (date + TimeSpanToConsiderExchangeRateOutdated < DateTime.UtcNow) then
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
                                   (Formatting.DecimalAmountRounding CurrencyType.Fiat fiatBalance)
                                   defaultFiatCurrency
        | NotFresh(Cached(usdValue,time)) ->
            let fiatBalance = usdValue * balance
            NotFresh(Cached(fiatBalance,time)),sprintf "~ %s %s%s"
                                                    (Formatting.DecimalAmountRounding CurrencyType.Fiat fiatBalance)
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
                sprintf "%A (?)" currency, NotFresh(NotAvailable), sprintf "(?) %s" defaultFiatCurrency
            | Some balanceAmount ->
                let cryptoAmount = Formatting.DecimalAmountRounding CurrencyType.Crypto balanceAmount
                let cryptoAmountStr = sprintf "%A %s" currency cryptoAmount
                let usdRate = FiatValueEstimation.UsdValue currency
                let fiatAmount,fiatAmountStr = BalanceInUsdString balanceAmount usdRate
                cryptoAmountStr,fiatAmount,fiatAmountStr
        Device.BeginInvokeOnMainThread(fun _ ->
            balanceLabel.Text <- balanceAmountStr
            fiatBalanceLabel.Text <- fiatAmountStr
        )
        fiatAmount

    let UpdateBalanceWithoutCacheAsync (balanceSet: BalanceSet) (mode: ServerSelectionMode) (cancelSource: CancellationTokenSource)
                                           : Async<BalanceState> =
        async {
            let! balance,imminentIncomingPayment =
                Account.GetShowableBalanceAndImminentIncomingPayment balanceSet.Account mode (Some cancelSource)
            let fiatAmount =
                UpdateBalance balance
                              balanceSet.Account.Currency
                              balanceSet.Widgets.CryptoLabel
                              balanceSet.Widgets.FiatLabel
            return {
                BalanceSet = balanceSet
                FiatAmount = fiatAmount
                ImminentIncomingPayment = imminentIncomingPayment
            }
        }

    let UpdateBalanceAsync (balanceSet: BalanceSet) (tryCachedFirst: bool) (mode: ServerSelectionMode)
                               : CancellationTokenSource*Async<BalanceState> =
        let cancelSource = new CancellationTokenSource()
        let job = async {
            if tryCachedFirst then
                let cachedBalance = Caching.Instance.RetrieveLastCompoundBalance balanceSet.Account.PublicAddress
                                                                                 balanceSet.Account.Currency
                match cachedBalance with
                | Cached _ ->
                    let fiatAmount =
                        UpdateBalance (NotFresh cachedBalance)
                                      balanceSet.Account.Currency
                                      balanceSet.Widgets.CryptoLabel
                                      balanceSet.Widgets.FiatLabel
                    return {
                        BalanceSet = balanceSet
                        FiatAmount = fiatAmount
                        ImminentIncomingPayment = None
                    }
                | _ ->
                    // FIXME: probably we can only load confirmed balances in this case (no need to check unconfirmed)
                    return! UpdateBalanceWithoutCacheAsync balanceSet mode cancelSource
            else
                return! UpdateBalanceWithoutCacheAsync balanceSet mode cancelSource
        }
        cancelSource,job

    let UpdateBalancesAsync accountBalances (tryCacheFirst: bool) (mode: ServerSelectionMode)
                                : seq<CancellationTokenSource>*Async<array<BalanceState>> =
        let sourcesAndJobs = seq {
            for balanceSet in accountBalances do
                let cancelSource,balanceJob = UpdateBalanceAsync balanceSet tryCacheFirst mode
                yield cancelSource,balanceJob
        }
        let parallelJobs =
            Seq.map snd sourcesAndJobs |> Async.Parallel
        let allCancelSources =
            Seq.map fst sourcesAndJobs
        allCancelSources,parallelJobs

    let private MaybeCrash (ex: Exception) =
        if null = ex then
            ()
        else
            let shouldCrash =
                true
                (* with no brute force cancellation, we might not need to catch TaskCanceledException anymore?
                if not BruteForceCancellationEnabled then
                    true
                elif (FSharpUtil.FindException<TaskCanceledException> ex).IsSome then
                    false
                else
                    true
                *)
            if shouldCrash then
                Device.BeginInvokeOnMainThread(fun _ ->
                    raise ex
                )

    // when running Task<unit> or Task<T> where we want to ignore the T, we should still make sure there is no exception,
    // & if there is, bring it to the main thread to fail fast, report to Sentry, etc, otherwise it gets ignored
    let DoubleCheckCompletion<'T> (task: Task<'T>) =
        task.ContinueWith(fun (t: Task<'T>) ->
            MaybeCrash t.Exception
        , TaskContinuationOptions.OnlyOnFaulted) |> ignore
    let DoubleCheckCompletionNonGeneric (task: Task) =
        task.ContinueWith(fun (t: Task) ->
            MaybeCrash t.Exception
        , TaskContinuationOptions.OnlyOnFaulted) |> ignore

    let DoubleCheckCompletionAsync<'T> (work: Async<'T>): unit =
        async {
            try
                let! _ = work
                ()
            with
            | ex ->
                MaybeCrash ex
            return ()
        } |> Async.Start

    let SwitchToNewPageDiscardingCurrentOne (currentPage: Page) (newPage: Page): unit =
        Device.BeginInvokeOnMainThread(fun _ ->
            NavigationPage.SetHasNavigationBar(newPage, false)

            //workaround for https://github.com/xamarin/Xamarin.Forms/issues/4030 FIXME: remove it when bug is fixed
            if Device.RuntimePlatform = Device.macOS then
                currentPage.Navigation.PushAsync newPage
                    |> DoubleCheckCompletionNonGeneric
            else
                currentPage.Navigation.InsertPageBefore(newPage, currentPage)
                currentPage.Navigation.PopAsync()
                    |> DoubleCheckCompletion
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

    let internal GetCryptoColor(currency: Currency) =
        match currency with
        | Currency.BTC -> Color.FromRgb(245, 146, 47)
        | Currency.DAI -> Color.FromRgb(254, 205, 83)
        | Currency.ETC -> Color.FromRgb(14, 119, 52)
        | Currency.ETH -> Color.FromRgb(130, 131, 132)
        | Currency.LTC -> Color.FromRgb(54, 94, 155)

    let internal ApplyGtkWorkaroundForFrameTransparentBackgroundColor (frame: Frame) =
        if enableGtkWorkarounds && (Device.RuntimePlatform = Device.GTK) then
            let ubuntu1804DefaultColor = "F2F1F0"

            // this is just most popular distro's default colour, we should rather just fix the upstream bug:
            // https://github.com/xamarin/Xamarin.Forms/issues/4700
            frame.BackgroundColor <- Color.FromHex ubuntu1804DefaultColor
            frame.BorderColor <- Color.FromHex ubuntu1804DefaultColor

    let internal ApplyGtkWorkarounds (balanceLabel: Label) (adjustSize: bool) =
        // workaround to small default fonts in GTK (compared to other toolkits) so FIXME: file bug about this
        if enableGtkWorkarounds && (Device.RuntimePlatform = Device.GTK) && adjustSize then
            balanceLabel.FontSize <- MagicGtkNumber

        if enableGtkWorkarounds && (Device.RuntimePlatform = Device.GTK) then
            // workaround about Labels not putting a decent default left&top margin in GTK so FIXME: file bug about this
            balanceLabel.TranslationY <- MagicGtkNumber
            balanceLabel.TranslationX <- MagicGtkNumber
            // workaround about Labels is not centered vertically inside layout
            balanceLabel.VerticalOptions <- LayoutOptions.FillAndExpand

        balanceLabel

    let private CreateLabelWidgetForAccount horizontalOptions =
        let label = Label(Text = "...",
                          VerticalOptions = LayoutOptions.Center,
                          HorizontalOptions = horizontalOptions)
        ApplyGtkWorkarounds label true

    let private normalCryptoBalanceClassId = "normalCryptoBalanceFrame"
    let private readonlyCryptoBalanceClassId = "readonlyCryptoBalanceFrame"
    let GetActiveAndInactiveCurrencyClassIds readOnly =
        if readOnly then
            readonlyCryptoBalanceClassId,normalCryptoBalanceClassId
        else
            normalCryptoBalanceClassId,readonlyCryptoBalanceClassId

    let CreateCurrencyBalanceFrame currency (cryptoLabel: Label) (fiatLabel: Label) currencyLogoImg classId =
        let colorBoxWidth = 10.

        let stackLayout = StackLayout(Orientation = StackOrientation.Horizontal,
                                      Padding = Thickness(20., 20., colorBoxWidth + 10., 20.))

        stackLayout.Children.Add currencyLogoImg
        stackLayout.Children.Add cryptoLabel
        stackLayout.Children.Add fiatLabel

        let colorBox = BoxView(Color = GetCryptoColor currency)

        let absoluteLayout = AbsoluteLayout(Margin = Thickness(0., 1., 3., 1.))
        absoluteLayout.Children.Add(stackLayout, Rectangle(0., 0., 1., 1.), AbsoluteLayoutFlags.All)
        absoluteLayout.Children.Add(colorBox, Rectangle(1., 0., colorBoxWidth, 1.), AbsoluteLayoutFlags.PositionProportional ||| AbsoluteLayoutFlags.HeightProportional)

        if Device.RuntimePlatform = Device.GTK then
            //workaround about GTK ScrollView's scroll bar. Not sure if it's bug indeed.
            absoluteLayout.Margin <- Thickness(absoluteLayout.Margin.Left, absoluteLayout.Margin.Top, 20., absoluteLayout.Margin.Bottom)
            //workaround about GTK layouting. It ignores margins of parent layout. So, we have to duplicate them
            stackLayout.Margin <- Thickness(stackLayout.Margin.Left, stackLayout.Margin.Top, 20., stackLayout.Margin.Bottom)

        //TODO: remove this workaround once https://github.com/xamarin/Xamarin.Forms/pull/5207 is merged
        if Device.RuntimePlatform = Device.macOS then
            let bindImageSize bindableProperty =
                let binding = Binding(Path = "Height", Source = cryptoLabel)
                currencyLogoImg.SetBinding(bindableProperty, binding)

            bindImageSize VisualElement.WidthRequestProperty
            bindImageSize VisualElement.HeightRequestProperty

        let frame = Frame(HasShadow = false,
                          ClassId = classId,
                          Content = absoluteLayout,
                          Padding = Thickness(0.),
                          BorderColor = Color.SeaShell)
        frame

    let private CreateWidgetsForAccount (currency: Currency) currencyLogoImg classId: BalanceWidgets =
        let accountBalanceLabel = CreateLabelWidgetForAccount LayoutOptions.Start
        let fiatBalanceLabel = CreateLabelWidgetForAccount LayoutOptions.EndAndExpand

        {
            CryptoLabel = accountBalanceLabel
            FiatLabel = fiatBalanceLabel
            Frame = CreateCurrencyBalanceFrame currency accountBalanceLabel fiatBalanceLabel currencyLogoImg classId
        }

    let CreateWidgetsForAccounts(accounts: seq<IAccount>) (currencyImages: Map<Currency*bool,Image>) readOnly
                                    : List<BalanceSet> =
        let classId,_ = GetActiveAndInactiveCurrencyClassIds readOnly
        seq {
            for account in accounts do
                let currencyLogoImg = currencyImages.[(account.Currency,readOnly)]
                let balanceWidgets = CreateWidgetsForAccount account.Currency currencyLogoImg classId
                yield {
                    Account = account;
                    Widgets = balanceWidgets
                }
        } |> List.ofSeq

    let BarCodeScanningOptions = MobileBarcodeScanningOptions(
                                     TryHarder = Nullable<bool> true,
                                     DisableAutofocus = false,
                                     // TODO: stop using Sys.Coll.Gen when this PR is accepted: https://github.com/Redth/ZXing.Net.Mobile/pull/800
                                     PossibleFormats = System.Collections.Generic.List<BarcodeFormat>(
                                         [ BarcodeFormat.QR_CODE ]
                                     ),
                                     UseNativeScanning = true
                                 )

    let GetImageSource name =
        let thisAssembly = typeof<BalanceState>.Assembly
        let thisAssemblyName = thisAssembly.GetName().Name
        let fullyQualifiedResourceNameForLogo = sprintf "%s.img.%s.png"
                                                        thisAssemblyName name
        ImageSource.FromResource(fullyQualifiedResourceNameForLogo, thisAssembly)

    let GetSizedImageSource name size =
        let sizedName = sprintf "%s_%ix%i" name size size
        GetImageSource sizedName

    let GetSizedColoredImageSource name color size =
        let sizedColoredName = sprintf "%s_%s" name color
        GetSizedImageSource sizedColoredName size

