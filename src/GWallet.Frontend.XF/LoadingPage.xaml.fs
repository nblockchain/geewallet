namespace GWallet.Frontend.XF

open System
open System.Linq

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Backend

type LoadingPage(state: FrontendHelpers.IGlobalAppState, showLogo: bool) as this =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<LoadingPage>)

    let mainLayout = base.FindByName<StackLayout> "mainLayout"
    let titleLabel = mainLayout.FindByName<Label> "titleLabel"
    let loadingLabel = mainLayout.FindByName<Label> "loadingLabel"

    let allAccounts = Account.GetAllActiveAccounts()
    let normalAccounts = allAccounts.OfType<NormalAccount>() |> List.ofSeq
                         |> List.map (fun account -> account :> IAccount)
    let readOnlyAccounts = allAccounts.OfType<ReadOnlyAccount>() |> List.ofSeq
                           |> List.map (fun account -> account :> IAccount)

    let CreateImage (currency: Currency) (readOnly: bool) =
        let colour =
            if readOnly then
                "grey"
            else
                "red"
        let currencyLowerCase = currency.ToString().ToLower()
        let imageSource = FrontendHelpers.GetSizedColoredImageSource currencyLowerCase colour 60
        let currencyLogoImg = Image(Source = imageSource, IsVisible = true)
        currencyLogoImg
    let GetAllCurrencyCases(): seq<Currency*bool> =
        seq {
            for currency in Currency.GetAll() do
                yield currency,true
                yield currency,false
        }
    let GetAllImages(): seq<(Currency*bool)*Image> =
        seq {
            for currency,readOnly in GetAllCurrencyCases() do
                yield (currency,readOnly),(CreateImage currency readOnly)
        }
    let PreLoadCurrencyImages(): Async<Map<Currency*bool,Image>> =
        async {
            return (GetAllImages() |> Map.ofSeq)
        }

    let logoImageSource = FrontendHelpers.GetSizedImageSource "logo" 512
    let logoImg = Image(Source = logoImageSource, IsVisible = true)
    let ShowLoadingText() =
        Device.BeginInvokeOnMainThread(fun _ ->
            mainLayout.VerticalOptions <- LayoutOptions.Center
            mainLayout.Padding <- Thickness(20.,0.,20.,50.)
            logoImg.IsVisible <- false
            titleLabel.IsVisible <- true
            loadingLabel.IsVisible <- true
        )

    do
        this.Init()

    [<Obsolete(DummyPageConstructorHelper.Warning)>]
    new() = LoadingPage(DummyPageConstructorHelper.GlobalFuncToRaiseExceptionIfUsedAtRuntime(),false)

    member this.Init (): unit =

        if showLogo then
            mainLayout.Children.Add logoImg

            Device.StartTimer(TimeSpan.FromSeconds 8.0, fun _ ->
                ShowLoadingText()

                false
            )
        else
            ShowLoadingText()

        let normalAccountsBalances = FrontendHelpers.CreateWidgetsForAccounts normalAccounts
        let allNormalAccountBalancesJob = FrontendHelpers.UpdateBalancesAsync normalAccountsBalances false

        let readOnlyAccountsBalances = FrontendHelpers.CreateWidgetsForAccounts readOnlyAccounts
        let readOnlyAccountBalancesJob = FrontendHelpers.UpdateBalancesAsync readOnlyAccountsBalances true

        let populateGrid = async {
            let! allBalancesJob =
                Async.Parallel(allNormalAccountBalancesJob::(readOnlyAccountBalancesJob::List.Empty))
                    |> Async.StartChild
            let! preloadCurrencyImagesJob = PreLoadCurrencyImages()
                                            |> Async.StartChild

            let! allResolvedBalances = allBalancesJob
            let allResolvedNormalAccountBalances = allResolvedBalances.ElementAt(0)
            let allResolvedReadOnlyBalances = allResolvedBalances.ElementAt(1)

            let! currencyImages = preloadCurrencyImagesJob

            Device.BeginInvokeOnMainThread(fun _ ->
                let balancesPage = BalancesPage(state, allResolvedNormalAccountBalances, allResolvedReadOnlyBalances,
                                                currencyImages, false)
                FrontendHelpers.SwitchToNewPageDiscardingCurrentOne this balancesPage
            )
        }
        Async.StartAsTask populateGrid
            |> FrontendHelpers.DoubleCheckCompletion

        ()

