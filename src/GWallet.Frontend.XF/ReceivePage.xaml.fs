namespace GWallet.Frontend.XF

open System

open Xamarin.Forms
open Xamarin.Forms.Xaml


type ReceivePage() =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<ReceivePage>)

    member this.OnSendPaymentClicked(sender: Object, args: EventArgs) =
        let sendPage = SendPage()

        NavigationPage.SetHasNavigationBar(sendPage, false)
        let navSendPage = NavigationPage sendPage
        NavigationPage.SetHasNavigationBar(navSendPage, false)

        this.Navigation.PushAsync navSendPage
            |> FrontendHelpers.DoubleCheckCompletionNonGeneric
