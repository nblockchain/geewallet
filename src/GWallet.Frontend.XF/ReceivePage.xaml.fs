namespace GWallet.Frontend.XF

open System

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Backend

type ReceivePage() =

    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<ReceivePage>)

    member this.OnSendPaymentClicked(_sender: Object, _args: EventArgs) =
        let sendPage () =
            SendPage() :> Page

        FrontendHelpers.SwitchToNewPage this sendPage false

