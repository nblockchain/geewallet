#if !XAMARIN
namespace GWallet.Frontend.Maui
#else
namespace GWallet.Frontend.XF
#endif
open System
open System.Linq
open System.Threading.Tasks

#if !XAMARIN
open Microsoft.Maui
open Microsoft.Maui.Graphics
open Microsoft.Maui.Controls
open Microsoft.Maui.ApplicationModel
open Microsoft.Maui.Layouts
type Color = Microsoft.Maui.Graphics.Colors
// added because of deprecated expansion options for StackLayout in using LayoutOptions.FillAndExpand
#nowarn "44"
#else
open Xamarin.Forms
open Xamarin.Essentials
open ZXing
open ZXing.Mobile
#endif
open Fsdk
open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks

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
    UsdRate: MaybeCached<decimal>
}

type internal CurrencyImageSize =
| Small = 60
| Big = 120

module FrontendHelpers =

    type IGlobalAppState =
        [<CLIEvent>]
        abstract member Resumed: IEvent<unit> with get
        [<CLIEvent>]
        abstract member GoneToSleep: IEvent<unit> with get

    type IAugmentablePayPage =
        abstract member AddTransactionScanner: unit -> unit
#if XAMARIN
    let IsDesktop() =
        match Device.RuntimePlatform with
        | Device.Android | Device.iOS ->
            false
        | Device.macOS | Device.GTK | Device.UWP | Device.WPF ->
            true
        | _ ->
            // TODO: report a sentry warning
            false
#endif

    let PlatformIsCapableOfBarCodeScanning =
        Device.RuntimePlatform = Device.Android || Device.RuntimePlatform = Device.iOS

    let internal BigFontSize = 22.

    let internal MediumFontSize = 20.

    let private defaultFiatCurrency = "USD"

    let internal ExchangeRateUnreachableMsg = SPrintF1 " (~ ? %s)" defaultFiatCurrency

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
            Fresh(fiatBalance),SPrintF2 "~ %s %s"
                                   (Formatting.DecimalAmountRounding CurrencyType.Fiat fiatBalance)
                                   defaultFiatCurrency
        | NotFresh(Cached(usdValue,time)) ->
            let fiatBalance = usdValue * balance
            NotFresh(Cached(fiatBalance,time)),SPrintF3 "~ %s %s%s"
                                                    (Formatting.DecimalAmountRounding CurrencyType.Fiat fiatBalance)
                                                    defaultFiatCurrency
                                                    (MaybeReturnOutdatedMarkForOldDate time)

    let internal GetCryptoColor(currency: Currency) =
        match currency with
        | Currency.BTC -> Color.FromRgb(245, 146, 47)

        // looks very similar to BTC (orangish)... so let's use SAI color when we phase it out?
        | Currency.DAI -> Color.FromRgb(250, 176, 28)

        | Currency.SAI -> Color.FromRgb(254, 205, 83)
        | Currency.ETC -> Color.FromRgb(14, 119, 52)
        | Currency.ETH -> Color.FromRgb(130, 131, 132)
        | Currency.LTC -> Color.FromRgb(54, 94, 155)

    let UpdateBalance (balance: MaybeCached<decimal>) currency usdRate
                      (maybeFrame: Option<Frame>) (balanceLabel: Label) (fiatBalanceLabel: Label)
                      (cryptoSubUnit: Option<UtxoCoin.SubUnit>)
                          : MaybeCached<decimal> =
        let maybeBalanceAmount =
            match balance with
            | NotFresh(NotAvailable) ->
                None
            | NotFresh(Cached(amount,_)) ->
                Some amount
            | Fresh(amount) ->
                match maybeFrame, currency, amount with
                | Some frame, Currency.SAI, 0m ->
                    MainThread.BeginInvokeOnMainThread(fun _ ->
                        frame.IsVisible <- false
                    )
                | _ -> ()
                Some amount
        let balanceAmountStr,fiatAmount,fiatAmountStr =
            match maybeBalanceAmount with
            | None ->
                SPrintF1 "%A (?)" currency, NotFresh(NotAvailable), SPrintF1 "(?) %s" defaultFiatCurrency
            | Some balanceAmount ->
                let adjustedBalance =
                    match cryptoSubUnit with
                    | None -> balanceAmount
                    | Some unit ->
                        balanceAmount * decimal unit.Multiplier
                let cryptoAmount = Formatting.DecimalAmountRounding CurrencyType.Crypto adjustedBalance
                let cryptoAmountStr =
                    match cryptoSubUnit with
                    | None ->
                        SPrintF2 "%A %s" currency cryptoAmount
                    | Some unit ->
                        SPrintF2 "%s %A" cryptoAmount unit.Caption
                let fiatAmount,fiatAmountStr = BalanceInUsdString balanceAmount usdRate
                cryptoAmountStr,fiatAmount,fiatAmountStr
        MainThread.BeginInvokeOnMainThread(fun _ ->
            balanceLabel.Text <- balanceAmountStr
            fiatBalanceLabel.Text <- fiatAmountStr
        )
        fiatAmount

    let UpdateBalanceWithoutCacheAsync (balanceSet: BalanceSet)
                                       (mode: ServerSelectionMode)
                                       (cancelSource: CustomCancelSource)
                                           : Async<BalanceState> =
        async {
            let balanceJob =
                Account.GetShowableBalanceAndImminentIncomingPayment balanceSet.Account mode (Some cancelSource)
            let usdRateJob = FiatValueEstimation.UsdValue balanceSet.Account.Currency

            let bothJobs = FSharpUtil.AsyncExtensions.MixedParallel2 balanceJob usdRateJob
            let! bothResults = bothJobs
            let (balance,imminentIncomingPayment),usdRate = bothResults

            let fiatAmount =
                UpdateBalance balance
                              balanceSet.Account.Currency
                              usdRate
                              (Some balanceSet.Widgets.Frame)
                              balanceSet.Widgets.CryptoLabel
                              balanceSet.Widgets.FiatLabel
                              None
            return {
                BalanceSet = balanceSet
                FiatAmount = fiatAmount
                ImminentIncomingPayment = imminentIncomingPayment
                UsdRate = usdRate
            }
        }

    let UpdateBalanceAsync (balanceSet: BalanceSet)
                           (tryCachedFirst: bool)
                           (mode: ServerSelectionMode)
                           (maybeProgressBar: Option<StackLayout>)
                               : CustomCancelSource*Async<BalanceState> =
        let cancelSource = new CustomCancelSource()
        let job = async {
            if tryCachedFirst then
                let cachedBalance = Caching.Instance.RetrieveLastCompoundBalance balanceSet.Account.PublicAddress
                                                                                 balanceSet.Account.Currency
                match cachedBalance with
                | Cached _ ->
                    let! usdRate = FiatValueEstimation.UsdValue balanceSet.Account.Currency
                    let fiatAmount =
                        UpdateBalance (NotFresh cachedBalance)
                                      balanceSet.Account.Currency
                                      usdRate
                                      (Some balanceSet.Widgets.Frame)
                                      balanceSet.Widgets.CryptoLabel
                                      balanceSet.Widgets.FiatLabel
                                      None
                    return {
                        BalanceSet = balanceSet
                        FiatAmount = fiatAmount
                        ImminentIncomingPayment = None
                        UsdRate = usdRate
                    }
                | _ ->
                    // FIXME: probably we can only load confirmed balances in this case (no need to check unconfirmed)
                    return! UpdateBalanceWithoutCacheAsync balanceSet mode cancelSource
            else
                return! UpdateBalanceWithoutCacheAsync balanceSet mode cancelSource
        }
        let fullJob =
            let UpdateProgressBar (progressBar: StackLayout) =
                MainThread.BeginInvokeOnMainThread(fun _ ->
                    let progressBarChildren =
#if XAMARIN
                        progressBar.Children
#else
                        progressBar |> Seq.choose(function | :? View as view -> Some view | _ -> None )
#endif
                    let firstTransparentFrameFound =
                        progressBarChildren.First(fun x -> x.BackgroundColor = Color.Transparent)
                    firstTransparentFrameFound.BackgroundColor <- GetCryptoColor balanceSet.Account.Currency
                )
            async {
                try
                    let! jobResult = job

                    match maybeProgressBar with
                    | Some progressBar ->
                        UpdateProgressBar progressBar
                    | None -> ()

                    return jobResult
                finally
                    (cancelSource:>IDisposable).Dispose()
            }
        cancelSource,fullJob

    let UpdateBalancesAsync accountBalances
                            (tryCacheFirst: bool)
                            (mode: ServerSelectionMode)
                            (progressBar: Option<StackLayout>)
                                : seq<CustomCancelSource>*Async<array<BalanceState>> =
        let sourcesAndJobs = seq {
            for balanceSet in accountBalances do
                let cancelSource,balanceJob = UpdateBalanceAsync balanceSet tryCacheFirst mode progressBar
                yield cancelSource,balanceJob
        }
        let parallelJobs =
            Seq.map snd sourcesAndJobs |> Async.Parallel
        let allCancelSources =
            Seq.map fst sourcesAndJobs
        allCancelSources,parallelJobs
    let private MaybeCrash (canBeCanceled: bool) (ex: Exception) =
        let LastResortBail() =
            // this is just in case the raise(throw) doesn't really tear down the program:
            Infrastructure.LogError ("FATAL PROBLEM: " + ex.ToString())
            Infrastructure.LogError "MANUAL FORCED SHUTDOWN NOW"
#if XAMARIN
            Device.PlatformServices.QuitApplication()
#else
            Application.Current.Quit()
#endif

        if isNull ex then
            ()
        else
            let shouldCrash =
                if not canBeCanceled then
                    true
                elif (FSharpUtil.FindException<TaskCanceledException> ex).IsSome then
                    false
                else
                    true
            if shouldCrash then
                MainThread.BeginInvokeOnMainThread(fun _ ->
                    raise ex
                    LastResortBail()
                )
                raise ex
                LastResortBail()

    // when running Task<unit> or Task<T> where we want to ignore the T, we should still make sure there is no exception,
    // & if there is, bring it to the main thread to fail fast, report to Sentry, etc, otherwise it gets ignored
    let DoubleCheckCompletion<'T> (task: Task<'T>) =
        task.ContinueWith(fun (t: Task<'T>) ->
            MaybeCrash false t.Exception
        , TaskContinuationOptions.OnlyOnFaulted) |> ignore
    let DoubleCheckCompletionNonGeneric (task: Task) =
        task.ContinueWith(fun (t: Task) ->
            MaybeCrash false t.Exception
        , TaskContinuationOptions.OnlyOnFaulted) |> ignore

    let DoubleCheckCompletionAsync<'T> (canBeCanceled: bool) (work: Async<'T>): unit =
        async {
            try
                let! _ = work
                ()
            with
            | ex ->
                MaybeCrash canBeCanceled ex
            return ()
        } |> Async.Start

    let SwitchToNewPage (currentPage: Page) (createNewPage: unit -> Page) (navBar: bool): unit =
        MainThread.BeginInvokeOnMainThread(fun _ ->
            let newPage = createNewPage ()
            NavigationPage.SetHasNavigationBar(newPage, false)
            let navPage = NavigationPage newPage
            NavigationPage.SetHasNavigationBar(navPage, navBar)

            currentPage.Navigation.PushAsync navPage
                |> DoubleCheckCompletionNonGeneric
        )

    let SwitchToNewPageDiscardingCurrentOne (currentPage: Page) (createNewPage: unit -> Page): unit =
        MainThread.BeginInvokeOnMainThread(fun _ ->
            let newPage = createNewPage ()
            NavigationPage.SetHasNavigationBar(newPage, false)

            currentPage.Navigation.InsertPageBefore(newPage, currentPage)
            currentPage.Navigation.PopAsync()
                |> DoubleCheckCompletion
        )

    let SwitchToNewPageDiscardingCurrentOneAsync (currentPage: Page) (createNewPage: unit -> Page) =
        async {
            let newPage = createNewPage ()
            NavigationPage.SetHasNavigationBar(newPage, false)

            currentPage.Navigation.InsertPageBefore(newPage, currentPage)
            let! _ =
                currentPage.Navigation.PopAsync()
                |> Async.AwaitTask
            return ()
        }

    let ChangeTextAndChangeBack (button: Button) (newText: string) =
        let initialText = button.Text
        button.IsEnabled <- false
        button.Text <- newText
        Task.Run(fun _ ->
            Task.Delay(TimeSpan.FromSeconds(2.0)).Wait()
            MainThread.BeginInvokeOnMainThread(fun _ ->
                button.Text <- initialText
                button.IsEnabled <- true
            )
        ) |> DoubleCheckCompletionNonGeneric

    let private CreateLabelWidgetForAccount horizontalOptions =
        let label = Label(Text = "...",
                          VerticalOptions = LayoutOptions.Center,
                          HorizontalOptions = horizontalOptions)
        label

    let private normalCryptoBalanceClassId = "normalCryptoBalanceFrame"
    let private readonlyCryptoBalanceClassId = "readonlyCryptoBalanceFrame"
    let GetActiveAndInactiveCurrencyClassIds readOnly =
        if readOnly then
            readonlyCryptoBalanceClassId,normalCryptoBalanceClassId
        else
            normalCryptoBalanceClassId,readonlyCryptoBalanceClassId

    let CreateCurrencyBalanceFrame currency (cryptoLabel: Label) (fiatLabel: Label) (currencyLogoImg: View) classId =
        let colorBoxWidth = 10.

        let outerLayout =
#if XAMARIN
            // because of layout changes in the currency list colorBoxWidth for XF needs to be larger 
            // for color box to be visible
            let colorBoxWidth = colorBoxWidth * 2.0
            let stackLayout = StackLayout(Orientation = StackOrientation.Horizontal,
                                          Padding = Thickness(20., 20., colorBoxWidth + 10., 20.))

            stackLayout.Children.Add currencyLogoImg
            stackLayout.Children.Add cryptoLabel
            stackLayout.Children.Add fiatLabel

            let colorBox = BoxView(Color = GetCryptoColor currency)

            let absoluteLayout = AbsoluteLayout(Margin = Thickness(0., 1., 3., 1.))
           
            absoluteLayout.Children.Add(stackLayout, Rectangle(0., 0., 1., 1.), AbsoluteLayoutFlags.All)
            absoluteLayout.Children.Add(colorBox, Rectangle(1., 0., colorBoxWidth, 1.), AbsoluteLayoutFlags.PositionProportional ||| AbsoluteLayoutFlags.HeightProportional)

            absoluteLayout
#else
            let innerLayout = Grid(Padding = Thickness(20., 20., 10., 20.))
            innerLayout.ColumnDefinitions.Add(ColumnDefinition(GridLength(currencyLogoImg.WidthRequest + 10.0)))
            innerLayout.ColumnDefinitions.Add(ColumnDefinition(GridLength.Auto))
            innerLayout.ColumnDefinitions.Add(ColumnDefinition())
            
            innerLayout.Add(currencyLogoImg, 0)
            innerLayout.Add(cryptoLabel, 1)
            innerLayout.Add(fiatLabel, 2)

            let outerLayout = Grid(Margin = Thickness(0., 1., 3., 1.))
            outerLayout.ColumnDefinitions.Add(ColumnDefinition())
            outerLayout.ColumnDefinitions.Add(ColumnDefinition(colorBoxWidth))

            let colorBox = BoxView(Color = GetCryptoColor currency)
            outerLayout.Add(innerLayout, 0)
            outerLayout.Add(colorBox, 1)
            outerLayout
#endif
#if XAMARIN
        //TODO: remove this workaround once https://github.com/xamarin/Xamarin.Forms/pull/5207 is merged
        if Device.RuntimePlatform = Device.macOS then
            let bindImageSize bindableProperty =
                let binding = Binding(Path = "Height", Source = cryptoLabel)
                currencyLogoImg.SetBinding(bindableProperty, binding)

            bindImageSize VisualElement.WidthRequestProperty
            bindImageSize VisualElement.HeightRequestProperty
#endif
        let frame = Frame(HasShadow = false,
                          ClassId = classId,
                          Content = outerLayout,
                          Padding = Thickness(0.),
                          BorderColor = Color.SeaShell)
        frame

    let private CreateWidgetsForAccount (currency: Currency) currencyLogoImg classId: BalanceWidgets =
        let accountBalanceLabel = CreateLabelWidgetForAccount LayoutOptions.Start
        // TODO: [FS0044] This construct is deprecated. The StackLayout expansion options are deprecated; please use a Grid instead.
        // Should remove #nowarn 44 after fixing this.
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
#if XAMARIN
    let BarCodeScanningOptions = MobileBarcodeScanningOptions(
                                     TryHarder = Nullable<bool> true,
                                     DisableAutofocus = false,
                                     // TODO: stop using Sys.Coll.Gen when this PR is accepted: https://github.com/Redth/ZXing.Net.Mobile/pull/800
                                     PossibleFormats = System.Collections.Generic.List<BarcodeFormat>(
                                         [ BarcodeFormat.QR_CODE ]
                                     ),
                                     UseNativeScanning = true
                                 )
#endif
    let GetImageSource name =
        let thisAssembly = typeof<BalanceState>.Assembly
        let thisAssemblyName = thisAssembly.GetName().Name
        let fullyQualifiedResourceNameForLogo = SPrintF2 "%s.img.%s.png"
                                                        thisAssemblyName name
        ImageSource.FromResource(fullyQualifiedResourceNameForLogo, thisAssembly)

    let internal GetSizedImageSource name (size: int) =
        let sizedName = SPrintF3 "%s_%ix%i" name size size
        GetImageSource sizedName

    let internal GetSizedColoredImageSource name color (size: CurrencyImageSize) =
        let sizedColoredName = SPrintF2 "%s_%s" name color
        GetSizedImageSource sizedColoredName (int size)

    let internal CreateCurrencyImageSource (currency: Currency) (readOnly: bool) (size: CurrencyImageSize) =
        let colour =
            if readOnly then
                "grey"
            else
                "red"
        let currencyLowerCase = currency.ToString().ToLower()
        GetSizedColoredImageSource currencyLowerCase colour size

    let internal CreateCurrencyImage (currency: Currency) (readOnly: bool) (size: CurrencyImageSize) =
        let imageSource = CreateCurrencyImageSource currency readOnly size
        let currencyLogoImg = Image(Source = imageSource, IsVisible = true)
#if !XAMARIN
        let imageSizeOnScreen = float(size) / 2.0
        currencyLogoImg.WidthRequest <- imageSizeOnScreen
        currencyLogoImg.HeightRequest <- imageSizeOnScreen
#endif
        currencyLogoImg

    let StartTimer(interval: TimeSpan, action: unit -> bool) =
#if XAMARIN
        Device.StartTimer(interval, Func<bool> action)
#else
        let timer = Application.Current.Dispatcher.CreateTimer(Interval = interval, IsRepeating = true)
        timer.Tick.Add(fun _ ->
            if not <| action() then timer.Stop() )
        timer.Start()
#endif
    let DefaultDesktopWindowSize = 500, 1000
