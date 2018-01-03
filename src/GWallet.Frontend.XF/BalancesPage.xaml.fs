namespace GWallet.Frontend.XF

open System
open System.Threading.Tasks

open Xamarin.Forms
open Xamarin.Forms.Xaml
open Plugin.Clipboard

open GWallet.Backend

type BalancesPage() =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<BalancesPage>)

    let mainLayout = base.FindByName<StackLayout>("mainLayout")
    let accounts = GWallet.Backend.Account.GetAllActiveAccounts()
    do
        let grid = Grid()
        let defaultGridLength = GridLength(1.0, GridUnitType.Star)

        let columnDef1 = ColumnDefinition()
        columnDef1.Width <- defaultGridLength
        grid.ColumnDefinitions.Add(columnDef1)
        let columnDef2 = ColumnDefinition()
        columnDef2.Width <- defaultGridLength
        grid.ColumnDefinitions.Add(columnDef2)
        let columnDef3 = ColumnDefinition()
        columnDef3.Width <- defaultGridLength
        grid.ColumnDefinitions.Add(columnDef3)

        grid.ColumnSpacing <- 30.0
        mainLayout.Children.Add(grid)

        let mutable rowCount = 0 //TODO: do this recursively instead of imperatively
        for account in accounts do
            let rowDefinition = RowDefinition()
            rowDefinition.Height <- defaultGridLength
            grid.RowDefinitions.Add(rowDefinition)

            let balance = Account.GetBalance account

            let sendButton = Button()
            sendButton.Text <- "Send"
            sendButton.IsEnabled <- false

            let balanceAmount =
                match balance with
                | NotFresh(NotAvailable) -> "?"
                | NotFresh(Cached(amount,_)) -> amount.ToString()
                | Fresh(amount) ->
                    if (amount > 0.0m) then
                        sendButton.IsEnabled <- true
                    amount.ToString()
            let accountBalance = Label()

            accountBalance.Text <- sprintf "%s %s" balanceAmount (account.Currency.ToString())
            let receiveButton = Button()
            receiveButton.Text <- "Receive"
            receiveButton.Clicked.Subscribe(fun _ ->
                CrossClipboard.Current.SetText account.PublicAddress
                receiveButton.IsEnabled <- false
                receiveButton.Text <- "Copied"
                Task.Run(fun _ ->
                    Task.Delay(TimeSpan.FromSeconds(2.0)).Wait()
                    Device.BeginInvokeOnMainThread(fun _ ->
                        receiveButton.Text <- "Receive"
                        receiveButton.IsEnabled <- true
                    )
                ) |> ignore

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
