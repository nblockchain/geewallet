namespace GWallet.Frontend.Maui

open Microsoft.Maui.Controls
open Microsoft.Maui.Controls.Xaml

type App() as this =
    inherit Application()

    do this.LoadFromXaml typeof<App> |> ignore<App>
    do this.MainPage <- AppShell()
