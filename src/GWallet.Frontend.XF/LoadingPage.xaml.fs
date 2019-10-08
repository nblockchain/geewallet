namespace GWallet.Frontend.XF

open System
open System.Linq
open System.Threading.Tasks

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Backend

/// <param name="showLogoFirst">
/// true  if just the logo should be shown first, and title text and loading text after some seconds,
/// false if title text and loading text should be shown immediatly.
/// </param>
type LoadingPage(state: FrontendHelpers.IGlobalAppState, showLogoFirst: bool) as this =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<LoadingPage>)

    let mainLayout = base.FindByName<StackLayout> "mainLayout"
    let titleLabel = mainLayout.FindByName<Label> "titleLabel"
    let progressBarLayout = base.FindByName<StackLayout> "progressBarLayout"
    let loadingLabel = mainLayout.FindByName<Label> "loadingLabel"
    let dotsMaxCount = 3
    let loadingTextNoDots = loadingLabel.Text
    let animLength = TimeSpan.FromMilliseconds 500.

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
    let PreLoadCurrencyImages(): Map<Currency*bool,Image> =
        GetAllImages() |> Map.ofSeq

    let logoImageSource = FrontendHelpers.GetSizedImageSource "logo" 512
    let logoImg = Image(Source = logoImageSource, IsVisible = true)

    let mutable keepAnimationTimerActive = true

    let UpdateDotsLabel() =
        Device.BeginInvokeOnMainThread(fun _ ->
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
        Device.BeginInvokeOnMainThread(fun _ ->
            mainLayout.VerticalOptions <- LayoutOptions.Center
            mainLayout.Padding <- Thickness(20.,0.,20.,50.)
            logoImg.IsVisible <- false
            titleLabel.IsVisible <- true
            progressBarLayout.IsVisible <- true
            loadingLabel.IsVisible <- true
        )
        Device.StartTimer(animLength, Func<bool> UpdateDotsLabel)

    do
        this.Init()

    [<Obsolete(DummyPageConstructorHelper.Warning)>]
    new() = LoadingPage(DummyPageConstructorHelper.GlobalFuncToRaiseExceptionIfUsedAtRuntime(),false)

    member this.Init (): unit =

        if showLogoFirst then
            mainLayout.Children.Add logoImg

            Device.StartTimer(TimeSpan.FromSeconds 8.0, fun _ ->
                ShowLoadingText()

                false // do not run timer again
            )
        else
            ShowLoadingText()

        let currencyImages = PreLoadCurrencyImages()

        let normalAccountsBalances = FrontendHelpers.CreateWidgetsForAccounts normalAccounts currencyImages false
        let _,allNormalAccountBalancesJob = FrontendHelpers.UpdateBalancesAsync normalAccountsBalances
                                                                                false
                                                                                ServerSelectionMode.Fast
                                                                                (Some progressBarLayout)
        let allNormalAccountBalancesJobAugmented = async {
            try
                let! normalAccountBalances = allNormalAccountBalancesJob
                return normalAccountBalances
            with
            | ex when (FSharpUtil.FindException<TaskCanceledException> ex).IsSome ->
                // TODO: remove this below once we finishing tracking down (fixing)
                //       https://gitlab.com/knocte/geewallet/issues/125
                return raise <| InvalidOperationException("Cancellation at normal-account first-query", ex)
        }

        let readOnlyAccountsBalances = FrontendHelpers.CreateWidgetsForAccounts readOnlyAccounts currencyImages true
        let _,readOnlyAccountBalancesJob =
            FrontendHelpers.UpdateBalancesAsync readOnlyAccountsBalances true ServerSelectionMode.Fast None
        let readOnlyAccountBalancesJobAugmented = async {
            try
                let! readOnlyAccountBalances = readOnlyAccountBalancesJob
                return readOnlyAccountBalances
            with
            | ex when (FSharpUtil.FindException<TaskCanceledException> ex).IsSome ->
                // TODO: remove this below once we finishing tracking down (fixing)
                //       https://gitlab.com/knocte/geewallet/issues/125
                return raise <| InvalidOperationException("Cancellation at readonly-account first-query", ex)
        }

        let currencyImages = PreLoadCurrencyImages()

        let populateGrid = async {
            let bothJobs = FSharpUtil.AsyncExtensions.MixedParallel2 allNormalAccountBalancesJobAugmented
                                                                     readOnlyAccountBalancesJobAugmented

            try
                let! allResolvedNormalAccountBalances,allResolvedReadOnlyBalances = bothJobs

                keepAnimationTimerActive <- false

                Device.BeginInvokeOnMainThread(fun _ ->
                    try
                        let balancesPage = BalancesPage(state, allResolvedNormalAccountBalances, allResolvedReadOnlyBalances,
                                                        currencyImages, false)
                        FrontendHelpers.SwitchToNewPageDiscardingCurrentOne this balancesPage
                    with
                    | ex when (FSharpUtil.FindException<TaskCanceledException> ex).IsSome ->
                        // TODO: remove this below once we finishing tracking down (fixing)
                        //       https://gitlab.com/knocte/geewallet/issues/125
                        raise <| InvalidOperationException("Cancellation at first-balances page population", ex)
                )
            with
            | ex when (FSharpUtil.FindException<TaskCanceledException> ex).IsSome ->
                // TODO: remove this below once we finishing tracking down (fixing)
                //       https://gitlab.com/knocte/geewallet/issues/125
                return raise <| InvalidOperationException("Cancellation at first-balances querying", ex)
        }
        Async.StartAsTask populateGrid
            |> FrontendHelpers.DoubleCheckCompletion

        ()

