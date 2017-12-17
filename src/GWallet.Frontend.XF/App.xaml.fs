namespace GWallet.Frontend.XF

open System.Linq

open Xamarin.Forms

open GWallet.Backend

module Initialization =
    let LandingPage(): ContentPage =
        let accounts = Account.GetAllActiveAccounts()
        if not (accounts.Any()) then
            WelcomePage() :> ContentPage
        else
            BalancesPage() :> ContentPage

type App() =
    inherit Application(MainPage = Initialization.LandingPage())
