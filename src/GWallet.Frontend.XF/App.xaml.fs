namespace GWallet.Frontend.XF

open Xamarin.Forms

type App() =
    inherit Application(MainPage = Initialization.LandingPage())

    override this.OnSleep(): unit =
        Initialization.GlobalState.FireGoneToSleep()

    override this.OnResume(): unit =
        Initialization.GlobalState.FireResumed()
