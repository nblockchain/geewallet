namespace GWallet.Frontend.Maui

open Android.App
open Microsoft.Maui

[<Application>]
type MainApplication(handle, ownership) =
    inherit MauiApplication(handle, ownership)

    do GWallet.Frontend.Maui.Resource.UpdateIdValues()
    
    override _.CreateMauiApp() = MauiProgram.CreateMauiApp()