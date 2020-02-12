namespace GWallet.Frontend.XF

open System

open Xamarin.Forms
open Xamarin.Forms.Xaml


type ReceivePage() =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<ReceivePage>)

    let mainLayout = base.FindByName<StackLayout> "mainLayout"
    let theLabel = mainLayout.FindByName<Label> "theLabel"
    do
        Device.BeginInvokeOnMainThread(fun _ ->
            // workaround for bug https://github.com/xamarin/Xamarin.Forms/issues/9526
            theLabel.TextColor <- Color.Black
        )

    member this.OnSendPaymentClicked(sender: Object, args: EventArgs) =
        ()

    member this.OnCopyToClipboardClicked(sender: Object, args: EventArgs) =
        ()
