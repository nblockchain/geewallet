namespace GWallet.Frontend.XF

open System

open Xamarin.Forms
open Xamarin.Forms.Xaml


type ReceivePage() =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<ReceivePage>)


    member this.OnSendPaymentClicked(sender: Object, args: EventArgs) =
        ()

    member this.OnCopyToClipboardClicked(sender: Object, args: EventArgs) =
        ()
