namespace GWallet.Frontend.XF

open System
open System.Linq

open Xamarin.Forms
open Xamarin.Forms.Xaml
open Xamarin.Essentials
open Fsdk

open GWallet.Backend

/// <param name="showLogoFirst">
/// true  if just the logo should be shown first, and title text and loading text after some seconds,
/// false if title text and loading text should be shown immediatly.
/// </param>
type LoadingPage(state: FrontendHelpers.IGlobalAppState, showLogoFirst: bool) as self =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<LoadingPage>)

    let mainLayout = base.FindByName<Grid> "mainLayout"
    let titleLabel = mainLayout.FindByName<Label> "titleLabel"
    let progressBarLayout = base.FindByName<StackLayout> "progressBarLayout"
    let loadingLabel = mainLayout.FindByName<Label> "loadingLabel"
    let dotsMaxCount = 3
    let loadingTextNoDots = loadingLabel.Text

    let allAccounts = Account.GetAllActiveAccounts()
    let normalAccounts = allAccounts.OfType<NormalAccount>() |> List.ofSeq
                         |> List.map (fun account -> account :> IAccount)
    let readOnlyAccounts = allAccounts.OfType<ReadOnlyAccount>() |> List.ofSeq
                           |> List.map (fun account -> account :> IAccount)

    let GetAllCurrencyCases(): seq<Currency*bool> =
        seq {
            for currency in Currency.GetAll() do
                yield currency,true
                yield currency,false
        }
    let GetAllImages(): seq<(Currency*bool)*Image> =
        seq {
            for currency,readOnly in GetAllCurrencyCases() do
                let currencyLogo =
                    FrontendHelpers.CreateCurrencyImage
                        currency
                        readOnly
                        CurrencyImageSize.Small
                yield (currency, readOnly), currencyLogo
        }
    let PreLoadCurrencyImages(): Map<Currency*bool,Image> =
        GetAllImages() |> Map.ofSeq

    let logoImageSource = FrontendHelpers.GetSizedImageSource "logo" 512
    let logoImg = Image(Source = logoImageSource, IsVisible = true)

    let mutable keepAnimationTimerActive = true

    let UpdateDotsLabel() =
        MainThread.BeginInvokeOnMainThread(fun _ ->
            let currentCountPlusOne = loadingLabel.Text.Count(fun x -> x = '.') + 1
            let dotsCount =
                if currentCountPlusOne > dotsMaxCount then
                    0
                else
                    currentCountPlusOne
            let dotsSeq = Enumerable.Repeat('.', dotsCount)
            loadingLabel.Text <- loadingTextNoDots + String(dotsSeq.ToArray())
        )
        keepAnimationTimerActive

    let ShowLoadingText() =
        MainThread.BeginInvokeOnMainThread(fun _ ->
            mainLayout.VerticalOptions <- LayoutOptions.Center
            mainLayout.Padding <- Thickness(20.,0.,20.,50.)
            logoImg.IsVisible <- false
            titleLabel.IsVisible <- true
            progressBarLayout.IsVisible <- true
            loadingLabel.IsVisible <- true
        )

        let dotsAnimationLength = TimeSpan.FromMilliseconds 500.
        Device.StartTimer(dotsAnimationLength, Func<bool> UpdateDotsLabel)
    do
        self.Init()

    [<Obsolete(DummyPageConstructorHelper.Warning)>]
    new() = LoadingPage(DummyPageConstructorHelper.GlobalFuncToRaiseExceptionIfUsedAtRuntime(),false)

    member self.Transition(): unit =
        let currencyImages = PreLoadCurrencyImages()

        let normalAccountsBalances = FrontendHelpers.CreateWidgetsForAccounts normalAccounts currencyImages false
        let _,allNormalAccountBalancesJob = FrontendHelpers.UpdateBalancesAsync normalAccountsBalances
                                                                                false
                                                                                ServerSelectionMode.Fast
                                                                                (Some progressBarLayout)

        let readOnlyAccountsBalances = FrontendHelpers.CreateWidgetsForAccounts readOnlyAccounts currencyImages true
        let _cancelSources, readOnlyAccountBalancesJob =
            FrontendHelpers.UpdateBalancesAsync readOnlyAccountsBalances true ServerSelectionMode.Fast None

        async {
            let bothJobs =
                FSharpUtil.AsyncExtensions.MixedParallel2
                    allNormalAccountBalancesJob
                    readOnlyAccountBalancesJob

            let! allResolvedNormalAccountBalances,allResolvedReadOnlyBalances = bothJobs

            keepAnimationTimerActive <- false

            let balancesPage () =
                BalancesPage(state, allResolvedNormalAccountBalances, allResolvedReadOnlyBalances,
                             currencyImages, false)
                    :> Page
            FrontendHelpers.SwitchToNewPageDiscardingCurrentOne self balancesPage
        }
            |> FrontendHelpers.DoubleCheckCompletionAsync false

        ()

    member self.Init (): unit =
        if showLogoFirst then
            MainThread.BeginInvokeOnMainThread(fun _ ->
                mainLayout.Children.Add logoImg
            )

            self.Transition()

            Device.StartTimer(TimeSpan.FromSeconds 5.0, fun _ ->
                ShowLoadingText()

                false // do not run timer again
            )
        else
            ShowLoadingText()

            self.Transition()

