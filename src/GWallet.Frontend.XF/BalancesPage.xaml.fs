namespace GWallet.Frontend.XF

open Xamarin.Forms
open Xamarin.Forms.Xaml

type BalancesPage()
                      as this =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<BalancesPage>)

    let mainLayout = base.FindByName<StackLayout> "mainLayout"
    let theLabel = mainLayout.FindByName<Label> "theLabel"

    do
        this.Init()

    member this.UpdateLabel (label: Label) =
        let tapGestureRecognizer = TapGestureRecognizer()
        tapGestureRecognizer.Tapped.Subscribe(fun _ ->
            let receivePage = ReceivePage()
            NavigationPage.SetHasNavigationBar(receivePage, false)
            let navPage = NavigationPage receivePage

            this.Navigation.PushAsync navPage
                 |> FrontendHelpers.DoubleCheckCompletionNonGeneric
        ) |> ignore
        label.GestureRecognizers.Add tapGestureRecognizer


    member private this.Init () =
        Device.BeginInvokeOnMainThread(fun _ ->
            this.UpdateLabel theLabel
        )
