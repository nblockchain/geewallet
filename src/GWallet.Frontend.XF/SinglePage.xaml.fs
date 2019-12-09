namespace GWallet.Frontend.XF

open Xamarin.Forms
open Xamarin.Forms.Xaml

type SinglePage() =
    inherit ContentPage()

    do
        base.LoadFromXaml(typeof<SinglePage>) |> ignore

