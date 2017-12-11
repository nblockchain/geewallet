namespace GWallet.Frontend.XF

open Xamarin.Forms
open Xamarin.Forms.Xaml

type AppPage() =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<AppPage>)
