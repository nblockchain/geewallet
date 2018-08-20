namespace GWallet.Frontend.XF

open System
open System.Linq

open Xamarin.Forms
open Xamarin.Forms.Xaml
open Plugin.Connectivity

open GWallet.Backend

type CycleStart =
    | ImmediateForAll
    | ImmediateForReadOnlyAccounts
    | Delayed

type BalancesPage(state: FrontendHelpers.IGlobalAppState,
                  normalAccountsAndBalances: seq<IAccount*Label*Label*MaybeCached<decimal>>,
                  readOnlyAccountsAndBalances: seq<IAccount*Label*Label*MaybeCached<decimal>>,
                  startWithReadOnlyAccounts: bool)
                      as this =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<BalancesPage>)

    let mainLayout = base.FindByName<StackLayout>("mainLayout")
    let totalFiatAmountLabel = mainLayout.FindByName<Label> "totalFiatAmountLabel"
    let totalReadOnlyFiatAmountLabel = mainLayout.FindByName<Label> "totalReadOnlyFiatAmountLabel"

    let timeToRefreshBalances = TimeSpan.FromSeconds 60.0

    let GetBalanceUpdateJobs accountsAndBalances =
        seq {
            for normalAccount,accountBalance,fiatBalance,_ in accountsAndBalances do
                yield FrontendHelpers.UpdateBalanceAsync normalAccount accountBalance fiatBalance
        }
    let normalBalancesJob = Async.Parallel (GetBalanceUpdateJobs normalAccountsAndBalances)
    let readOnlyBalancesJob = Async.Parallel (GetBalanceUpdateJobs readOnlyAccountsAndBalances)

    // FIXME: should reuse code with FrontendHelpers.BalanceInUsdString
    let UpdateGlobalFiatBalanceLabel (balance: MaybeCached<decimal>) (totalFiatAmountLabel: Label) =
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
        totalFiatAmountLabel.Text <- strBalance

    let rec UpdateGlobalFiatBalance (acc: MaybeCached<decimal>) fiatBalances totalFiatAmountLabel: unit =
        match acc with
        | NotFresh NotAvailable ->
            UpdateGlobalFiatBalanceLabel (NotFresh(NotAvailable)) totalFiatAmountLabel
        | Fresh accAmount ->
            match fiatBalances with
            | [] ->
                UpdateGlobalFiatBalanceLabel acc totalFiatAmountLabel
            | head::tail ->
                match head with
                | NotFresh NotAvailable ->
                    UpdateGlobalFiatBalanceLabel (NotFresh(NotAvailable)) totalFiatAmountLabel
                | Fresh newAmount ->
                    UpdateGlobalFiatBalance (Fresh (newAmount+accAmount)) tail totalFiatAmountLabel
                | NotFresh(Cached(newCachedAmount,time)) ->
                    UpdateGlobalFiatBalance (NotFresh(Cached(newCachedAmount+accAmount,time))) tail totalFiatAmountLabel
        | NotFresh(Cached(cachedAccAmount,accTime)) ->
            match fiatBalances with
            | [] ->
                UpdateGlobalFiatBalanceLabel acc totalFiatAmountLabel
            | head::tail ->
                match head with
                | NotFresh NotAvailable ->
                    UpdateGlobalFiatBalanceLabel (NotFresh(NotAvailable)) totalFiatAmountLabel
                | Fresh newAmount ->
                    UpdateGlobalFiatBalance (NotFresh(Cached(newAmount+cachedAccAmount,accTime))) tail totalFiatAmountLabel
                | NotFresh(Cached(newCachedAmount,time)) ->
                    UpdateGlobalFiatBalance (NotFresh(Cached(newCachedAmount+cachedAccAmount,min accTime time)))
                                            tail
                                            totalFiatAmountLabel

    let cryptoBalanceClassId = "cryptoBalanceFrame"

    let rec FindCryptoBalances (layout: StackLayout) (elements: List<View>) (resultsSoFar: List<Frame>): List<Frame> =
        match elements with
        | [] -> resultsSoFar
        | head::tail ->
            match head with
            | :? Frame as frame ->
                let newResults =
                    if frame.ClassId = cryptoBalanceClassId then
                        frame::resultsSoFar
                    else
                        resultsSoFar
                FindCryptoBalances layout tail newResults
            | _ ->
                FindCryptoBalances layout tail resultsSoFar

    let mutable timerRunning = false
    let lockObject = Object()

    do
        this.Init()


    member private this.IsTimerRunning
        with get() = lock lockObject (fun _ -> timerRunning)
         and set value = lock lockObject (fun _ -> timerRunning <- value)

    member this.PopulateBalances balances =

        let footerLabel = mainLayout.FindByName<Label> "footerLabel"
        mainLayout.Children.Remove footerLabel |> ignore

        let currentCryptoBalances = FindCryptoBalances mainLayout (mainLayout.Children |> List.ofSeq) []
        for currentCryptoBalance in currentCryptoBalances do
            mainLayout.Children.Remove currentCryptoBalance |> ignore

        for account,cryptoBalance,fiatBalance,_ in balances do

            let tapGestureRecognizer = TapGestureRecognizer()
            tapGestureRecognizer.Tapped.Subscribe(fun _ ->
                let receivePage = ReceivePage(account, this)
                NavigationPage.SetHasNavigationBar(receivePage, false)
                let navPage = NavigationPage receivePage

                this.Navigation.PushAsync navPage
                     |> FrontendHelpers.DoubleCheckCompletionNonGeneric
            ) |> ignore

            let stackLayout = StackLayout(Orientation = StackOrientation.Horizontal)
            stackLayout.Children.Add cryptoBalance
            stackLayout.Children.Add fiatBalance

            let frame = Frame(HasShadow = false,
                              ClassId = cryptoBalanceClassId,
                              Content = stackLayout,
                              BorderColor = Color.SeaShell)
            frame.GestureRecognizers.Add tapGestureRecognizer

            mainLayout.Children.Add frame

        mainLayout.Children.Add footerLabel

    member this.UpdateGlobalFiatBalanceSum (allFiatBalances: seq<MaybeCached<decimal>>) totalFiatAmountLabel =
        UpdateGlobalFiatBalance (Fresh(0.0m)) (allFiatBalances |> List.ofSeq) totalFiatAmountLabel

    member private this.RefreshBalancesAndCheckIfAwake (onlyReadOnlyAccounts: bool): bool =
        let awake = state.Awake
        if (awake) then
            async {
                if (state.Awake && (not onlyReadOnlyAccounts)) then
                    let! resolvedNormalBalances = normalBalancesJob
                    let normalFiatBalances = resolvedNormalBalances.Select(fun (_,_,_,f) -> f)
                    Device.BeginInvokeOnMainThread(fun _ ->
                        this.UpdateGlobalFiatBalanceSum normalFiatBalances totalFiatAmountLabel
                    )
                if (state.Awake) then
                    let! resolvedReadOnlyBalances = readOnlyBalancesJob
                    let readOnlyFiatBalances = resolvedReadOnlyBalances.Select(fun (_,_,_,f) -> f)
                    Device.BeginInvokeOnMainThread(fun _ ->
                        this.UpdateGlobalFiatBalanceSum readOnlyFiatBalances totalReadOnlyFiatAmountLabel
                    )

            } |> Async.StartAsTask |> FrontendHelpers.DoubleCheckCompletion

        awake

    member private this.StartTimer(): unit =
        if not (this.IsTimerRunning) then
            Device.StartTimer(timeToRefreshBalances, fun _ ->
                this.IsTimerRunning <- true
                let awake = this.RefreshBalancesAndCheckIfAwake false
                this.IsTimerRunning <- awake

                // to keep or stop timer recurring
                awake
            )

    member this.StartBalanceRefreshCycle (cycleStart: CycleStart) =
        let onlyReadOnlyAccounts = (cycleStart = CycleStart.ImmediateForReadOnlyAccounts)
        if ((cycleStart <> CycleStart.Delayed && this.RefreshBalancesAndCheckIfAwake onlyReadOnlyAccounts) || true) then
            this.StartTimer()

    member private this.ConfigureFiatAmountFrame (normalAccountsBalances: seq<IAccount*Label*Label*_>)
                                                 (readOnlyAccountsBalances: seq<IAccount*Label*Label*_>)
                                                 (readOnly: bool): TapGestureRecognizer =
        let totalCurrentFiatAmountFrameName,totalOtherFiatAmountFrameName =
            if readOnly then
                "totalReadOnlyFiatAmountFrame","totalFiatAmountFrame"
            else
                "totalFiatAmountFrame","totalReadOnlyFiatAmountFrame"

        let totalCurrentFiatAmountFrame,totalOtherFiatAmountFrame =
            mainLayout.FindByName<Frame> totalCurrentFiatAmountFrameName,
            mainLayout.FindByName<Frame> totalOtherFiatAmountFrameName

        let tapGestureRecognizer = TapGestureRecognizer()
        tapGestureRecognizer.Tapped.Add(fun _ ->

            let shouldNotOpenNewPage =
                if readOnly then
                    true
                else
                    readOnlyAccountsBalances.Any()

            if not CrossConnectivity.IsSupported then
                failwith "cross connectivity plugin not supported for this platform?"
            if shouldNotOpenNewPage then
                Device.BeginInvokeOnMainThread(fun _ ->
                    totalCurrentFiatAmountFrame.IsVisible <- false
                    totalOtherFiatAmountFrame.IsVisible <- true
                )
                if readOnly then
                    this.PopulateBalances normalAccountsBalances
                else
                    this.AssignColorLabels false
                    this.PopulateBalances readOnlyAccountsBalances
            else
                let coldStoragePage =
                    // FIXME: save IsConnected to cache at app startup, and if it has ever been connected to the
                    // internet, already consider it non-cold storage
                    use crossConnectivityInstance = CrossConnectivity.Current
                    if crossConnectivityInstance.IsConnected then
                        let newBalancesPageFunc = (fun (normalAccountsAndBalances,readOnlyAccountsAndBalances) ->
                            BalancesPage(state, normalAccountsAndBalances, readOnlyAccountsAndBalances, true) :> Page
                        )
                        let page = PairingToPage(this, normalAccountsAndBalances, newBalancesPageFunc) :> Page
                        NavigationPage.SetHasNavigationBar(page, false)
                        let navPage = NavigationPage page
                        NavigationPage.SetHasNavigationBar(navPage, false)
                        navPage :> Page
                    else
                        let normalAccountsAddresses = Account.GetAllActiveAccounts().OfType<NormalAccount>()
                                                        .Select(fun acc -> (acc:>IAccount).PublicAddress) |> Set.ofSeq
                        let addressesSeparatedByCommas = String.Join(",", normalAccountsAddresses)

                        let page = PairingFromPage(this, "Copy addresses to clipboard", addressesSeparatedByCommas, None)
                        NavigationPage.SetHasNavigationBar(page, false)
                        let navPage = NavigationPage page
                        navPage :> Page

                this.Navigation.PushAsync coldStoragePage
                     |> FrontendHelpers.DoubleCheckCompletionNonGeneric
        ) |> ignore
        totalCurrentFiatAmountFrame.GestureRecognizers.Add tapGestureRecognizer
        tapGestureRecognizer

    member this.PopulateGrid () =

        let tapper = this.ConfigureFiatAmountFrame normalAccountsAndBalances readOnlyAccountsAndBalances false
        this.ConfigureFiatAmountFrame normalAccountsAndBalances readOnlyAccountsAndBalances true |> ignore

        this.PopulateBalances normalAccountsAndBalances

        if startWithReadOnlyAccounts then
            tapper.SendTapped null

    member private this.AssignColorLabels (readOnly: bool) =
        let labels,color =
            if readOnly then
                let color = Color.DarkBlue
                totalReadOnlyFiatAmountLabel.TextColor <- color
                readOnlyAccountsAndBalances,color
            else
                let color = Color.DarkRed
                totalFiatAmountLabel.TextColor <- color
                normalAccountsAndBalances,color

        for _,readOnlyLabel1,readOnlyLabel2,_ in labels do
            readOnlyLabel1.TextColor <- color
            readOnlyLabel2.TextColor <- color

    member private this.Init () =
        let allNormalAccountFiatBalances = normalAccountsAndBalances.Select(fun (_,_,_,f) -> f) |> List.ofSeq
        let allReadOnlyAccountFiatBalances = readOnlyAccountsAndBalances.Select(fun (_,_,_,f) -> f) |> List.ofSeq

        Device.BeginInvokeOnMainThread(fun _ ->
            this.AssignColorLabels true
            if startWithReadOnlyAccounts then
                this.AssignColorLabels false

            this.PopulateGrid ()

            this.UpdateGlobalFiatBalanceSum allNormalAccountFiatBalances totalFiatAmountLabel
            this.UpdateGlobalFiatBalanceSum allReadOnlyAccountFiatBalances totalReadOnlyFiatAmountLabel
        )
        this.StartBalanceRefreshCycle CycleStart.ImmediateForReadOnlyAccounts
