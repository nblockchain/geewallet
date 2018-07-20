namespace GWallet.Frontend.XF

open System
open System.Linq

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Backend

type BalancesPage() =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<BalancesPage>)

    let mainLayout = base.FindByName<StackLayout>("mainLayout")
    let normalAccounts = GWallet.Backend.Account.GetAllActiveAccounts().OfType<NormalAccount>() |> List.ofSeq

    let timeToRefreshBalances = TimeSpan.FromSeconds 60.0

    let CreateWidgetsForAccount(account: NormalAccount): Label*Label =
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
                let label,button = CreateWidgetsForAccount normalAccount
                yield normalAccount,label,button
        } |> List.ofSeq

    // FIXME: should reuse code with FrontendHelpers.BalanceInUsdString
    let UpdateGlobalFiatBalanceLabel (balance: MaybeCached<decimal>) =
        let strBalance =
            match balance with
            | NotFresh NotAvailable ->
                "? USD"
            | Fresh amount ->
                sprintf "~ %s USD" (Formatting.DecimalAmount CurrencyType.Fiat amount)
            | NotFresh(Cached(cachedAmount,time)) ->
                sprintf "~ %s USD%s"
                       (Formatting.DecimalAmount CurrencyType.Fiat cachedAmount)
                       (FrontendHelpers.MaybeReturnOutdatedMarkForOldDate time)
        let totalFiatAmountLabel = mainLayout.FindByName<Label> "totalFiatAmountLabel"
        totalFiatAmountLabel.Text <- strBalance

    let rec UpdateGlobalFiatBalance (acc: MaybeCached<decimal>) fiatBalances =
        match acc with
        | NotFresh NotAvailable ->
            UpdateGlobalFiatBalanceLabel (NotFresh(NotAvailable))
        | Fresh accAmount ->
            match fiatBalances with
            | [] ->
                UpdateGlobalFiatBalanceLabel acc
            | head::tail ->
                match head with
                | NotFresh NotAvailable ->
                    UpdateGlobalFiatBalanceLabel (NotFresh(NotAvailable))
                | Fresh newAmount ->
                    UpdateGlobalFiatBalance (Fresh (newAmount+accAmount)) tail
                | NotFresh(Cached(newCachedAmount,time)) ->
                    UpdateGlobalFiatBalance (NotFresh(Cached(newCachedAmount+accAmount,time))) tail
        | NotFresh(Cached(cachedAccAmount,accTime)) ->
            match fiatBalances with
            | [] ->
                UpdateGlobalFiatBalanceLabel acc
            | head::tail ->
                match head with
                | NotFresh NotAvailable ->
                    UpdateGlobalFiatBalanceLabel (NotFresh(NotAvailable))
                | Fresh newAmount ->
                    UpdateGlobalFiatBalance (NotFresh(Cached(newAmount+cachedAccAmount,accTime))) tail
                | NotFresh(Cached(newCachedAmount,time)) ->
                    UpdateGlobalFiatBalance (NotFresh(Cached(newCachedAmount+cachedAccAmount,min accTime time))) tail

    member this.UpdateGlobalFiatBalanceSum (allFiatBalances: seq<MaybeCached<decimal>>) =
        UpdateGlobalFiatBalance (Fresh(0.0m)) (allFiatBalances |> List.ofSeq)

    member this.StartTimer() =
        Device.StartTimer(timeToRefreshBalances, fun _ ->
            async {
                let balanceUpdateJobs =
                    seq {
                        for normalAccount,accountBalance,fiatBalance in accountsAndBalances do
                            yield FrontendHelpers.UpdateBalanceAsync normalAccount accountBalance fiatBalance
                    }
                let allBalancesJob = Async.Parallel balanceUpdateJobs
                let! allFiatBalances = allBalancesJob
                Device.BeginInvokeOnMainThread(fun _ ->
                    this.UpdateGlobalFiatBalanceSum allFiatBalances
                )
            } |> Async.StartAsTask |> FrontendHelpers.DoubleCheckCompletion

            // to keep timer recurring
            true
        )

    member this.PopulateGrid (initialBalancesTasksWithDetails: seq<_*NormalAccount*Label*Label>) =

        let footerLabel = mainLayout.FindByName<Label> "footerLabel"
        mainLayout.Children.Remove footerLabel |> ignore

        for _,normalAccount,accountBalance,fiatBalance in initialBalancesTasksWithDetails do
            let account = normalAccount :> IAccount

            let tapGestureRecognizer = TapGestureRecognizer()
            tapGestureRecognizer.Tapped.Subscribe(fun _ ->
                let receivePage = ReceivePage(normalAccount, this)
                NavigationPage.SetHasNavigationBar(receivePage, false)
                let navPage = NavigationPage receivePage

                // workaround for https://github.com/xamarin/Xamarin.Forms/issues/3329 as Android has back button anyway
                if (Device.RuntimePlatform = Device.Android) then
                    NavigationPage.SetHasNavigationBar(navPage, false)

                this.Navigation.PushAsync navPage
                     |> FrontendHelpers.DoubleCheckCompletionNonGeneric
            ) |> ignore

            let stackLayout = StackLayout(Orientation = StackOrientation.Horizontal)
            stackLayout.Children.Add(accountBalance)
            let frame = Frame(HasShadow = false,
                              Content = stackLayout,
                              BorderColor = Color.SeaShell)
            frame.GestureRecognizers.Add tapGestureRecognizer
            stackLayout.Children.Add(fiatBalance)
            mainLayout.Children.Add(frame)

        mainLayout.Children.Add footerLabel

    member this.Init (allFiatBalances: seq<MaybeCached<decimal>>)
                     (initialBalancesTasksWithDetails: seq<_*NormalAccount*Label*Label>): unit =

        Device.BeginInvokeOnMainThread(fun _ ->
            this.PopulateGrid initialBalancesTasksWithDetails
            this.UpdateGlobalFiatBalanceSum allFiatBalances
        )
        this.StartTimer()


