namespace GWallet.Frontend.Maui

open Microsoft.Maui
open Microsoft.Maui.Controls
open Microsoft.Maui.Controls.Xaml

type App() as this =
    inherit Application()

    do this.LoadFromXaml typeof<App> |> ignore<App>
#if GTK
    // Set style here instead of Styles.xml because OnPlatform property doesn't recognize "Gtk" value
    do
        // Set padding for buttons to be consistent with other platforms as they have padding around buttons by default.
        let style = Style(typeof<Button>)
        style.Setters.Add(Button.PaddingProperty, 4.0)
        this.Resources.Add(style)
#endif
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
