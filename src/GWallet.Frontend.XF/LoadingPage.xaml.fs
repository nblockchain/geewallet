namespace GWallet.Frontend.XF

open System.Linq

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Backend

type LoadingPage(state: FrontendHelpers.IGlobalAppState) as this =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<LoadingPage>)

    let normalAccounts = GWallet.Backend.Account.GetAllActiveAccounts().OfType<NormalAccount>() |> List.ofSeq

    let CreateWidgetsForAccount (): Label*Label =
        let accountBalanceLabel = Label(Text = "...",
                                        VerticalOptions = LayoutOptions.Center,
                                        HorizontalOptions = LayoutOptions.Start)
        let fiatBalanceLabel = Label(Text = "...",
                                     VerticalOptions = LayoutOptions.Center,
                                     HorizontalOptions = LayoutOptions.EndAndExpand)

        // workaround to small default fonts in GTK (compared to other toolkits) so FIXME: file bug about this
        let magicGtkNumber = FrontendHelpers.MagicGtkNumber
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

    let accountsAndBalances: List<NormalAccount*Label*Label> =
        seq {
            for normalAccount in normalAccounts do
                let cryptoLabel,fiatLabel = CreateWidgetsForAccount ()
                yield normalAccount,cryptoLabel,fiatLabel
        } |> List.ofSeq

    do
        this.Init()

    member this.Init (): unit =

        let initialBalancesTasksWithDetails =
            seq {
                for normalAccount,accountBalanceLabel,fiatBalanceLabel in accountsAndBalances do
                    let balanceJob =
                        FrontendHelpers.UpdateBalanceAsync normalAccount accountBalanceLabel fiatBalanceLabel
                    yield balanceJob,normalAccount,accountBalanceLabel,fiatBalanceLabel
            }

        let allBalancesJob = Async.Parallel (initialBalancesTasksWithDetails |> Seq.map (fun (j,_,_,_) -> j))
        let populateGrid = async {
            let! allFiatBalances = allBalancesJob
            let balancesPage = BalancesPage(state, accountsAndBalances)
            balancesPage.Init allFiatBalances
            FrontendHelpers.SwitchToNewPageDiscardingCurrentOne this balancesPage
        }
        Async.StartAsTask populateGrid
            |> FrontendHelpers.DoubleCheckCompletion

        ()

