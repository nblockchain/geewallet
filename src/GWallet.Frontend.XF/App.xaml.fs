namespace GWallet.Frontend.XF

open System
open System.Linq

open Xamarin.Forms

open GWallet.Backend

type GlobalState() =
    let lockObject = Object()
    let mutable awake = false
    let GetAwake() =
        lock lockObject (fun _ ->
            awake
        )
    member this.SetAwake(leValue: bool): unit =
        lock lockObject (fun _ ->
            awake <- leValue
        )
    interface FrontendHelpers.IGlobalAppState with
        member val Awake = GetAwake() with get

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
        Initialization.GlobalState.SetAwake false

    override this.OnResume(): unit =
        Initialization.GlobalState.SetAwake true

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
            balancesPage.StartTimer()
        | None -> ()
