namespace GWallet.Frontend.XF

open System
open System.Linq
open System.Threading
open System.Threading.Tasks

open Xamarin.Forms
open Xamarin.Forms.Xaml

type BalancesPage() as this =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<BalancesPage>)

    let mainLayout = base.FindByName<StackLayout>("mainLayout")
    let contentLayout = base.FindByName<StackLayout> "contentLayout"

    do
        this.Init()

    member this.PopulateBar () =
        let tapGestureRecognizer = TapGestureRecognizer()
        tapGestureRecognizer.Tapped.Subscribe(fun _ ->
            let receivePage = ReceivePage()
            NavigationPage.SetHasNavigationBar(receivePage, false)
            let navPage = NavigationPage receivePage

            this.Navigation.PushAsync navPage
                 |> FrontendHelpers.DoubleCheckCompletionNonGeneric
        ) |> ignore
        let mainStackLayout = StackLayout(Orientation = StackOrientation.Horizontal,
                                          Padding = Thickness(20., 20., 10., 20.))
        mainStackLayout.Children.Add (Label(Text = "BAR"))
        let mainFrame = Frame(HasShadow = false,
                          Content = mainStackLayout,
                          Padding = Thickness(0.),
                          BorderColor = Color.SeaShell)
        mainFrame.GestureRecognizers.Add tapGestureRecognizer

        contentLayout.Children.Add mainFrame


    member private this.Init () =

        Device.BeginInvokeOnMainThread(fun _ ->
            this.PopulateBar()
        )
