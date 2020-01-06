namespace GWallet.Frontend.XF

open System
open System.Threading

open Xamarin.Forms
open Xamarin.Forms.Xaml

type SendPage() =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<SendPage>)

    let mainLayout = base.FindByName<StackLayout> "mainLayout"
    let button = mainLayout.FindByName<Button> "button"

    member private this.SomeFunc () = async {
        Device.BeginInvokeOnMainThread(fun _ ->
            this.DisplayAlert("Alert", "BAZ", "OK")
                 |> FrontendHelpers.DoubleCheckCompletionNonGeneric
        )
    }

    member private this.ToggleInputWidgetsEnabledOrDisabled (enabled: bool) =
        Device.BeginInvokeOnMainThread(fun _ ->
            if enabled && (not button.IsEnabled) then
                button.Text <- "reenabled"
            button.IsEnabled <- enabled
        )

    member this.OnButtonClicked(sender: Object, args: EventArgs): unit =

        this.ToggleInputWidgetsEnabledOrDisabled false

        async {
            do! this.SomeFunc ()
            this.ToggleInputWidgetsEnabledOrDisabled true
        }    |> Async.StartAsTask
             |> FrontendHelpers.DoubleCheckCompletionNonGeneric

