namespace GWallet.Frontend.XF

open System
open System.Linq

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Backend

type LoadingPage(state: FrontendHelpers.IGlobalAppState) as this =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<LoadingPage>)

    let allAccounts = Account.GetAllActiveAccounts()
    let normalAccounts = allAccounts.OfType<NormalAccount>() |> List.ofSeq
                         |> List.map (fun account -> account :> IAccount)
    let readOnlyAccounts = allAccounts.OfType<ReadOnlyAccount>() |> List.ofSeq
                           |> List.map (fun account -> account :> IAccount)

    let thisAssembly = typeof<GlobalState>.Assembly
    let thisAssemblyName = thisAssembly.GetName().Name
    let CreateImage (currency: Currency) (readOnly: bool) =
        let iconSize = (60).ToString()
        let colour =
            if readOnly then
                "grey"
            else
                "red"
        let currencyLowerCase = currency.ToString().ToLower()
        let fullyQualifiedResourceName = sprintf "%s.img.%s_%s_%sx%s.png"
                                                 thisAssemblyName currencyLowerCase colour iconSize iconSize
        let imageSource = ImageSource.FromResource(fullyQualifiedResourceName, thisAssembly)
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

    do
        this.Init()

    [<Obsolete(DummyPageConstructorHelper.Warning)>]
    new() = LoadingPage(DummyPageConstructorHelper.GlobalFuncToRaiseExceptionIfUsedAtRuntime())

    member this.Init (): unit =

        let normalAccountsWithLabels = FrontendHelpers.CreateWidgetsForAccounts normalAccounts
        let allNormalAccountBalancesJob =
            seq {
                for balanceSet in normalAccountsWithLabels do
                    let balanceJob =
                        FrontendHelpers.UpdateBalanceAsync balanceSet
                    yield balanceJob
            } |> Async.Parallel

        let readOnlyAccountsBalances = FrontendHelpers.CreateWidgetsForAccounts readOnlyAccounts
        let readOnlyAccountBalancesJob = FrontendHelpers.UpdateCachedBalancesAsync readOnlyAccountsBalances

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

