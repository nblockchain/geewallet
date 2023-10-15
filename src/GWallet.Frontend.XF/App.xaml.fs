namespace GWallet.Frontend.XF

open Xamarin.Forms

type App() =
    inherit Application(MainPage = Initialization.LandingPage())

    override __.OnSleep(): unit =
        Initialization.GlobalState.FireGoneToSleep()

    override __.OnResume(): unit =
        Initialization.GlobalState.FireResumed()
