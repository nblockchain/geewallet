namespace GWallet.Frontend.XF

open System
open System.Linq

open Xamarin.Forms

open GWallet.Backend

type GlobalState() =
    let lockObject = Object()
    let mutable awake = true
    member internal this.Awake
        with set value = lock lockObject (fun _ -> awake <- value)

    interface FrontendHelpers.IGlobalAppState with
        member this.Awake
            with get() = lock lockObject (fun _ -> awake)

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

type App() =
    inherit Application(MainPage = Initialization.LandingPage())

    override this.OnSleep(): unit =
        Initialization.GlobalState.Awake <- false
        Async.CancelDefaultToken()

    override this.OnResume(): unit =
        Initialization.GlobalState.Awake <- true

        let maybeBalancesPage =
            match this.MainPage with
            | :? BalancesPage as balancesPage ->
                Some balancesPage
            | :? NavigationPage as navPage ->
                match navPage.RootPage with
                | :? BalancesPage as balancesPage ->
                    Some balancesPage
                | _ ->
                    None
            | _ ->
                None

        match maybeBalancesPage with
        | Some balancesPage ->
            balancesPage.StartBalanceRefreshCycle true
        | None -> ()
