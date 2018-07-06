namespace GWallet.Frontend.XF

open System
open System.Linq
open System.Timers
open System.Threading.Tasks

open Xamarin.Forms
open Xamarin.Forms.Xaml
open Plugin.Clipboard

open GWallet.Backend

type BalancesPage() as this =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<BalancesPage>)

    let mainLayout = base.FindByName<StackLayout>("mainLayout")
    let normalAccounts = GWallet.Backend.Account.GetAllActiveAccounts().OfType<NormalAccount>() |> List.ofSeq

    let timeToRefreshBalances = TimeSpan.FromSeconds 60.0
    let balanceRefreshTimer = new Timer(timeToRefreshBalances.TotalMilliseconds)

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

    let totalFiatAmountLabel = Label(Text="...",
                                     FontSize = FrontendHelpers.BigFontSize,
                                     Margin = Thickness(0.,20.,0.,20.),
                                     VerticalOptions = LayoutOptions.CenterAndExpand,
                                     HorizontalOptions = LayoutOptions.Center)

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
    do
        this.Init()

    member this.UpdateGlobalFiatBalanceSum (allFiatBalances: seq<MaybeCached<decimal>>) =
        UpdateGlobalFiatBalance (Fresh(0.0m)) (allFiatBalances |> List.ofSeq)

    member this.UpdateBalance (normalAccount,balanceLabel: Label,fiatBalanceLabel: Label): Async<MaybeCached<decimal>> =
        async {
            let account = normalAccount :> IAccount
            let! balance = Account.GetShowableBalance normalAccount
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
                | None -> "?", NotFresh(NotAvailable), "?"
                | Some balanceAmount ->
                    let cryptoAmount = Formatting.DecimalAmount CurrencyType.Crypto balanceAmount
                    let cryptoAmountStr = sprintf "%s %A" cryptoAmount normalAccount.Currency
                    let usdRate = FiatValueEstimation.UsdValue normalAccount.Currency
                    let fiatAmount,fiatAmountStr = FrontendHelpers.BalanceInUsdString (balanceAmount, usdRate)
                    cryptoAmountStr,fiatAmount,fiatAmountStr
            Device.BeginInvokeOnMainThread(fun _ ->
                balanceLabel.Text <- balanceAmountStr
                fiatBalanceLabel.Text <- fiatAmountStr
            )
            return fiatAmount
        }

    member this.StartTimer() =
        balanceRefreshTimer.Elapsed.Add (fun _ ->
            async {
                let balanceUpdateJobs =
                    seq {
                        for normalAccount,accountBalance,fiatBalance in accountsAndBalances do
                            yield this.UpdateBalance (normalAccount,accountBalance,fiatBalance)
                    }
                let allBalancesJob = Async.Parallel balanceUpdateJobs
                let! allFiatBalances = allBalancesJob
                Device.BeginInvokeOnMainThread(fun _ ->
                    this.UpdateGlobalFiatBalanceSum allFiatBalances
                )
            } |> Async.StartAsTask |> FrontendHelpers.DoubleCheckCompletion
        )
        balanceRefreshTimer.Start()

    member this.PopulateGrid (initialBalancesTasksWithDetails: seq<_*NormalAccount*Label*Label>) =

        let titleLabel = mainLayout.FindByName<Label> "titleLabel"
        mainLayout.Children.Remove(mainLayout.FindByName<Label>("loadingLabel")) |> ignore
        mainLayout.VerticalOptions <- LayoutOptions.FillAndExpand
        mainLayout.Padding <- Thickness(0.)
        titleLabel.VerticalOptions <- LayoutOptions.Start

        titleLabel.HorizontalOptions <- LayoutOptions.Center
        titleLabel.Margin <- Thickness(0.,40.,0.,0.)

        mainLayout.Children.Add(totalFiatAmountLabel)

        for _,normalAccount,accountBalance,fiatBalance in initialBalancesTasksWithDetails do
            let account = normalAccount :> IAccount

            let tapGestureRecognizer = TapGestureRecognizer()
            tapGestureRecognizer.Tapped.Subscribe(fun _ ->
                this.Navigation.PushModalAsync(ReceivePage(normalAccount,accountBalance,fiatBalance))
                     |> FrontendHelpers.DoubleCheckCompletion
            ) |> ignore

            let stackLayout = StackLayout(Orientation = StackOrientation.Horizontal)
            stackLayout.Children.Add(accountBalance)
            let frame = Frame(HasShadow = false,
                              Content = stackLayout,
                              BorderColor = Color.SeaShell)
            frame.GestureRecognizers.Add tapGestureRecognizer
            stackLayout.Children.Add(fiatBalance)
            mainLayout.Children.Add(frame)

        let footerLabel = Label(Text = "www.diginex.com",
                                Margin = Thickness(0.,30.,0.,30.),
                                VerticalOptions = LayoutOptions.End,
                                HorizontalOptions = LayoutOptions.Center)
        mainLayout.Children.Add(footerLabel)


    member this.Init (): unit =

        let initialBalancesTasksWithDetails =
            seq {
                for normalAccount,accountBalanceLabel,fiatBalanceLabel in accountsAndBalances do
                    let balanceJob = this.UpdateBalance (normalAccount, accountBalanceLabel, fiatBalanceLabel)
                    yield balanceJob,normalAccount,accountBalanceLabel,fiatBalanceLabel
            }

        let allBalancesJob = Async.Parallel (initialBalancesTasksWithDetails |> Seq.map (fun (j,_,_,_) -> j))
        let populateGrid = async {
            let! allFiatBalances = allBalancesJob
            Device.BeginInvokeOnMainThread(fun _ ->
                this.PopulateGrid initialBalancesTasksWithDetails
                this.UpdateGlobalFiatBalanceSum allFiatBalances
            )
            this.StartTimer()
        }
        Async.StartAsTask populateGrid
            |> FrontendHelpers.DoubleCheckCompletion

        ()

