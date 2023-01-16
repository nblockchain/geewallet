namespace GWallet.Frontend.Maui

open Microsoft.Maui.Controls
open Microsoft.Maui.Controls.Xaml

type App() as this =
    inherit Application()
    let GlobalState = GlobalState()

    do this.LoadFromXaml(typeof<App>) |> ignore
    do this.MainPage <- (WelcomePage GlobalState) :> Page
