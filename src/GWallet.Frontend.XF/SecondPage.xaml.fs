namespace GWallet.Frontend.XF

open System

open Xamarin.Forms
open Xamarin.Forms.Xaml

type SecondPage() =

    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<SecondPage>)

    member this.OnSendPaymentClicked(_sender: Object, _args: EventArgs) =
        let thirdPage () =
            ThirdPage() :> Page

        FrontendHelpers.SwitchToNewPage this thirdPage false

