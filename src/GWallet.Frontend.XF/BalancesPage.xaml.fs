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
        for account in accounts do
            let balance = Account.GetBalance account
            let balanceAmount =
                match balance with
                | NotFresh(NotAvailable) -> "?"
                | NotFresh(Cached(amount,_)) -> amount.ToString()
                | Fresh(amount) -> amount.ToString()
            let accountBalance = Label()

            accountBalance.Text <- sprintf "%s %s" balanceAmount (account.Currency.ToString())
            let receiveButton = Button()
            receiveButton.Text <- "Receive"
            receiveButton.Clicked.Subscribe(fun _ ->
                CrossClipboard.Current.SetText account.PublicAddress
                receiveButton.IsEnabled <- false
                receiveButton.Text <- "Copied address to clipboard"
                Task.Run(fun _ ->
                    Task.Delay(TimeSpan.FromSeconds(2.0)).Wait()
                    Device.BeginInvokeOnMainThread(fun _ ->
                        receiveButton.Text <- "Receive"
                        receiveButton.IsEnabled <- true
                    )
                ) |> ignore

            ) |> ignore
            mainLayout.Children.Add(accountBalance)
            mainLayout.Children.Add(receiveButton)


