namespace GWallet.Frontend.XF

open System
open System.Linq

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Backend

type BalancesPage(state: FrontendHelpers.IGlobalAppState, accountsAndBalances: List<NormalAccount*Label*Label>) =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<BalancesPage>)

    let mainLayout = base.FindByName<StackLayout>("mainLayout")

    let timeToRefreshBalances = TimeSpan.FromSeconds 60.0

    let balanceUpdateJobs =
        seq {
            for normalAccount,accountBalance,fiatBalance in accountsAndBalances do
                yield FrontendHelpers.UpdateBalanceAsync normalAccount accountBalance fiatBalance
        }
    let allBalancesJob = Async.Parallel balanceUpdateJobs

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

    let mutable timerRunning = false
    let lockObject = Object()
    member private this.IsTimerRunning
        with get() = lock lockObject (fun _ -> timerRunning)
         and set value = lock lockObject (fun _ -> timerRunning <- value)

    member this.UpdateGlobalFiatBalanceSum (allFiatBalances: seq<MaybeCached<decimal>>) =
        UpdateGlobalFiatBalance (Fresh(0.0m)) (allFiatBalances |> List.ofSeq)

    member private this.RefreshBalancesAndCheckIfAwake(): bool =
        let awake = state.Awake
        if (awake) then
            async {
                if (state.Awake) then
                    let! allFiatBalances = allBalancesJob
                    if (state.Awake) then
                        Device.BeginInvokeOnMainThread(fun _ ->
                            this.UpdateGlobalFiatBalanceSum allFiatBalances
                        )
            } |> Async.StartAsTask |> FrontendHelpers.DoubleCheckCompletion

        awake

    member private this.StartTimer(): unit =
        if not (this.IsTimerRunning) then
            Device.StartTimer(timeToRefreshBalances, fun _ ->
                this.IsTimerRunning <- true
                let awake = this.RefreshBalancesAndCheckIfAwake()
                this.IsTimerRunning <- awake

                // to keep or stop timer recurring
                awake
            )

    member this.StartBalanceRefreshCycle (runImmediatelyToo: bool) =
        if ((runImmediatelyToo && this.RefreshBalancesAndCheckIfAwake()) || (not runImmediatelyToo)) then
            this.StartTimer()

    member this.PopulateGrid (accountsAndTheirLabels: seq<NormalAccount*Label*Label>) =

        let footerLabel = mainLayout.FindByName<Label> "footerLabel"
        mainLayout.Children.Remove footerLabel |> ignore

        for normalAccount,accountBalance,fiatBalance in accountsAndTheirLabels do
            let account = normalAccount :> IAccount

            let tapGestureRecognizer = TapGestureRecognizer()
            tapGestureRecognizer.Tapped.Subscribe(fun _ ->
                let receivePage = ReceivePage(normalAccount, this)
                NavigationPage.SetHasNavigationBar(receivePage, false)
                let navPage = NavigationPage receivePage

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
                         : unit =

        Device.BeginInvokeOnMainThread(fun _ ->
            this.PopulateGrid accountsAndBalances
            this.UpdateGlobalFiatBalanceSum allFiatBalances
        )
        this.StartBalanceRefreshCycle false


