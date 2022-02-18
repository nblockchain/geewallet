namespace GWallet.Frontend.XF

open Xamarin.Forms
open Xamarin.Forms.Xaml

type ThirdPage() =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<ThirdPage>)

    do
        ()