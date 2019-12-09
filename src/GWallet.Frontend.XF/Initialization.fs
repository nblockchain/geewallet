namespace GWallet.Frontend.XF

open Xamarin.Forms

module Initialization =

    let internal LandingPage(): NavigationPage =
        let landingPage = BalancesPage ()

        let navPage = NavigationPage landingPage
        NavigationPage.SetHasNavigationBar(landingPage, false)
        navPage
