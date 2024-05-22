#if XAMARIN
namespace GWallet.Frontend.XF
#else
namespace GWallet.Frontend.Maui
#endif

open System.Linq

#if XAMARIN
open Xamarin.Forms
#else
open Microsoft.Maui
open Microsoft.Maui.Controls
#endif

open GWallet.Backend

module Initialization =

    let internal GlobalState = GlobalState()

    let private GlobalInit () =
        Infrastructure.SetupExceptionHook ()

    let internal LandingPage(): NavigationPage =
        GlobalInit ()

        let accounts = Account.GetAllActiveAccounts()
        let landingPage:Page =
            if not (accounts.Any()) then
                (WelcomePage GlobalState) :> Page
            else
                (LoadingPage (GlobalState, true)) :> Page

        let navPage = NavigationPage landingPage
        NavigationPage.SetHasNavigationBar(landingPage, false)
        navPage
