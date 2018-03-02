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
    let normalAccounts = GWallet.Backend.Account.GetAllActiveAccounts().Cast<NormalAccount>()

    let timeToRefreshBalances = TimeSpan.FromSeconds 60.0
    let balanceRefreshTimer = new Timer(timeToRefreshBalances.TotalMilliseconds)

    let accountsAndBalances =
        seq {
            for normalAccount in normalAccounts do
                let accountBalanceLabel = Label(Text = "...")
                let sendButton = Button(Text = "Send",
                                        //FIXME: rather enable it always, and give error when balance is not fresh
                                        IsEnabled = false)
                yield normalAccount,accountBalanceLabel,sendButton
        } |> List.ofSeq

    do
        this.Init()

        balanceRefreshTimer.Elapsed.Add (fun _ ->
            for normalAccount,accountBalance,sendButton in accountsAndBalances do
                let balance = Account.GetShowableBalance normalAccount
                let account = normalAccount :> IAccount
                match balance with
                | Fresh(amount) ->
                    Device.BeginInvokeOnMainThread(fun _ ->
                        if (amount > 0.0m) then
                            sendButton.IsEnabled <- true
                        accountBalance.Text <- sprintf "%s %s" (amount.ToString()) (account.Currency.ToString())
                    )
                | _ -> ()
        )
        balanceRefreshTimer.Start()

    member this.Init() =
        let grid = Grid(ColumnSpacing = 1.0)
        let starGridLength = GridLength(1.0, GridUnitType.Star)
        let autoGridLength = GridLength(1.0, GridUnitType.Auto)

        let columnDef1 = ColumnDefinition(Width = starGridLength)
        grid.ColumnDefinitions.Add(columnDef1)
        let columnDef2 = ColumnDefinition(Width = starGridLength)
        grid.ColumnDefinitions.Add(columnDef2)
        let columnDef3 = ColumnDefinition(Width = autoGridLength)
        grid.ColumnDefinitions.Add(columnDef3)

        mainLayout.Children.Add(grid)

        let mutable rowCount = 0 //TODO: do this recursively instead of imperatively

        for normalAccount,accountBalance,sendButton in accountsAndBalances do
            let account = normalAccount :> IAccount

            let rowDefinition = RowDefinition(Height = starGridLength)
            grid.RowDefinitions.Add(rowDefinition)

            let balance = Account.GetShowableBalance account

            sendButton.Clicked.Subscribe(fun _ ->
                this.Navigation.PushModalAsync(SendPage(normalAccount)) |> FrontendHelpers.DoubleCheckCompletion
            ) |> ignore

            let balanceAmount =
                match balance with
                | NotFresh(NotAvailable) -> "?"
                | NotFresh(Cached(amount,_)) -> amount.ToString()
                | Fresh(amount) ->
                    if (amount > 0.0m) then
                        sendButton.IsEnabled <- true
                    amount.ToString()

            accountBalance.Text <- sprintf "%s %s" balanceAmount (account.Currency.ToString())
            let receiveButton = Button(Text = "Receive")
            receiveButton.Clicked.Subscribe(fun _ ->
            (* maybe the code below will be useful for platforms in which we cannot manage to render QR codes:

                CrossClipboard.Current.SetText account.PublicAddress
                receiveButton.IsEnabled <- false
                receiveButton.Text <- "Copied"
                Task.Run(fun _ ->
                    Task.Delay(TimeSpan.FromSeconds(2.0)).Wait()
                    Device.BeginInvokeOnMainThread(fun _ ->
                        receiveButton.Text <- "Receive"
                        receiveButton.IsEnabled <- true
                    )
                ) |> FrontendHelpers.DoubleCheckCompletion
            *)
                this.Navigation.PushModalAsync(ReceivePage(normalAccount)) |> FrontendHelpers.DoubleCheckCompletion
            ) |> ignore

            accountBalance.HorizontalOptions <- LayoutOptions.End
            accountBalance.VerticalOptions <- LayoutOptions.Center
            grid.Children.Add(accountBalance, 0, rowCount)

            sendButton.HorizontalOptions <- LayoutOptions.Center
            sendButton.VerticalOptions <- LayoutOptions.Center
            grid.Children.Add(sendButton, 1, rowCount)

            receiveButton.HorizontalOptions <- LayoutOptions.Start
            receiveButton.VerticalOptions <- LayoutOptions.Center
            grid.Children.Add(receiveButton, 2, rowCount)
            rowCount <- rowCount + 1

// idea taken from: https://stackoverflow.com/a/31456367/544947
#if DEBUG_LAYOUT
            accountBalance.BackgroundColor <- Color.Gray
            receiveButton.BackgroundColor <- Color.Beige
        grid.BackgroundColor <- Color.Brown
        if (grid.ColumnSpacing = 0) then
            grid.ColumnSpacing <- 0.5
        if (grid.RowSpacing = 0) then
            grid.RowSpacing <- 0.5
#endif
