namespace GWallet.Frontend.XF

open System.Linq

open Xamarin.Forms

open GWallet.Backend

module Initialization =

    let internal GlobalState = GlobalState()

    let private GlobalInit () =
        Infrastructure.SetupSentryHook ()

    let internal LandingPage(): NavigationPage =
        let state = GlobalInit ()

        let accounts = Account.GetAllActiveAccounts()
        let landingPage:Page =
            if not (accounts.Any()) then
                (WelcomePage GlobalState) :> Page
            else
                (LoadingPage GlobalState) :> Page

        let navPage = NavigationPage landingPage
        NavigationPage.SetHasNavigationBar(landingPage, false)
        navPage
