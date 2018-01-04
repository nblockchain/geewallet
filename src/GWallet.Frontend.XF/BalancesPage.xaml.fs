namespace GWallet.Frontend.XF

open System
open System.Linq
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

    do this.Init()

    member this.Init() =
        let grid = Grid()
        let starGridLength = GridLength(1.0, GridUnitType.Star)
        let autoGridLength = GridLength(1.0, GridUnitType.Auto)

        let columnDef1 = ColumnDefinition()
        columnDef1.Width <- starGridLength
        grid.ColumnDefinitions.Add(columnDef1)
        let columnDef2 = ColumnDefinition()
        columnDef2.Width <- starGridLength
        grid.ColumnDefinitions.Add(columnDef2)
        let columnDef3 = ColumnDefinition()
        columnDef3.Width <- autoGridLength
        grid.ColumnDefinitions.Add(columnDef3)

        grid.ColumnSpacing <- 1.0
        mainLayout.Children.Add(grid)

        let mutable rowCount = 0 //TODO: do this recursively instead of imperatively
        for normalAccount in normalAccounts do
            let account = normalAccount :> IAccount

            let rowDefinition = RowDefinition()
            rowDefinition.Height <- starGridLength
            grid.RowDefinitions.Add(rowDefinition)

            let balance = Account.GetBalance account

            let sendButton = Button()
            sendButton.Text <- "Send"
            sendButton.Clicked.Subscribe(fun _ ->
                this.Navigation.PushModalAsync(SendPage(normalAccount)) |> ignore
            ) |> ignore
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
