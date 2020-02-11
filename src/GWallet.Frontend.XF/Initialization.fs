namespace GWallet.Frontend.XF

open Xamarin.Forms

module Initialization =

    let internal LandingPage(): NavigationPage =
        let landingPage = BalancesPage ()
        NavigationPage.SetHasNavigationBar(landingPage, true)
        let navPage = NavigationPage landingPage
        NavigationPage.SetHasNavigationBar(navPage, true)
        navPage
