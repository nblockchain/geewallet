namespace GWallet.Frontend.XF

open Xamarin.Forms

module Initialization =

    let internal LandingPage(): NavigationPage =

        let landingPage:Page = (InitialPage ()) :> Page

        let navPage = NavigationPage landingPage
        NavigationPage.SetHasNavigationBar(landingPage, false)
        navPage

type App() =
    inherit Application(MainPage = Initialization.LandingPage())

