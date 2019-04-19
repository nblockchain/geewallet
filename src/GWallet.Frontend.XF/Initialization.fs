namespace GWallet.Frontend.XF

open Xamarin.Forms

module Initialization =

    let internal LandingPage(): Async<NavigationPage> =

        let populateGrid = async {
            let balancesPage = BalancesPage()
            let navPage = NavigationPage balancesPage
            NavigationPage.SetHasNavigationBar(balancesPage, false)
            return navPage
        }
        populateGrid
