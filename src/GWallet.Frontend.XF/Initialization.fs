namespace GWallet.Frontend.XF

open Xamarin.Forms

open GWallet.Backend

module Initialization =

    let private GlobalInit () =
        Infrastructure.SetupExceptionHook ()

    let internal LandingPage(): NavigationPage =
        GlobalInit ()

        let landingPage:Page = (LoadingPage true) :> Page

        let navPage = NavigationPage landingPage
        NavigationPage.SetHasNavigationBar(landingPage, false)
        navPage
