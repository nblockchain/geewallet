namespace GWallet.Frontend.XF

open System.Linq

open Xamarin.Forms

open GWallet.Backend

module Initialization =

    let private GlobalInit () =
        Infrastructure.SetupSentryHook ()

    let internal LandingPage(): NavigationPage =
        GlobalInit ()

        let accounts = Account.GetAllActiveAccounts()
        let landingPage:Page =
            if not (accounts.Any()) then
                WelcomePage() :> Page
            else
                LoadingPage() :> Page

        let navPage = NavigationPage landingPage
        NavigationPage.SetHasNavigationBar(landingPage, false)
        navPage

type App() =
    inherit Application(MainPage = Initialization.LandingPage())
