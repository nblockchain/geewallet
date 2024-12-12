﻿namespace GWallet.Frontend.Maui

open Microsoft.Maui
open Microsoft.Maui.Controls
open Microsoft.Maui.Controls.Xaml

type App() as this =
    inherit Application()

    do this.LoadFromXaml typeof<App> |> ignore<App>
    do this.MainPage <- Initialization.LandingPage()

#if GTK
    override _.CreateWindow(activationState) = 
        let window = base.CreateWindow(activationState)
        window.Created.Add(fun _ -> 
            let gtkWindow = MauiGtkApplication.Current.MainWindow
            gtkWindow.Resize FrontendHelpers.DefaultDesktopWindowSize
        )
        window.Title <- GWallet.Backend.Config.AppName
        window
#endif
