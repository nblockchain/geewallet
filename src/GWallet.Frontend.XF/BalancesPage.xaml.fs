namespace GWallet.Frontend.XF

open System

open Xamarin.Forms
open Xamarin.Forms.Xaml

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
            mainLayout.Children.Add(accountBalance)


