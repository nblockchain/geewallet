namespace GWallet.Frontend.XF

open System.Linq

open Xamarin.Forms

open GWallet.Backend

module Initialization =

    let private GlobalInit () =
        Infrastructure.SetupSentryHook ()

    let internal LandingPage(): Page =
        GlobalInit ()

        let accounts = Account.GetAllActiveAccounts()
        if not (accounts.Any()) then
            let welcomePage = WelcomePage()
            let navPage = NavigationPage welcomePage
            NavigationPage.SetHasNavigationBar(welcomePage, false)
            navPage :> Page
        else
            BalancesPage() :> Page

type App() =
    inherit Application(MainPage = Initialization.LandingPage())
