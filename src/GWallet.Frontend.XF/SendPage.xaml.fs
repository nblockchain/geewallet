namespace GWallet.Frontend.XF

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Backend

type SendPage() =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<SendPage>)

    do
        ()