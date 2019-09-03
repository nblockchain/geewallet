namespace GWallet.Frontend.XF

open System
open System.Linq
open System.Threading.Tasks

open Xamarin.Forms
open Xamarin.Forms.Xaml

module FrontendHelpers =

    let private MaybeCrash (ex: Exception) =
        if null = ex then
            ()
        else
            Device.BeginInvokeOnMainThread(fun _ ->
                raise ex
            )

    // when running Task<unit> or Task<T> where we want to ignore the T, we should still make sure there is no exception,
    // & if there is, bring it to the main thread to fail fast, report to Sentry, etc, otherwise it gets ignored
    let DoubleCheckCompletion<'T> (task: Task<'T>) =
        task.ContinueWith(fun (t: Task<'T>) ->
            MaybeCrash t.Exception
        , TaskContinuationOptions.OnlyOnFaulted) |> ignore
    let DoubleCheckCompletionNonGeneric (task: Task) =
        task.ContinueWith(fun (t: Task) ->
            MaybeCrash t.Exception
        , TaskContinuationOptions.OnlyOnFaulted) |> ignore

    let DoubleCheckCompletionAsync<'T> (work: Async<'T>): unit =
        async {
            try
                let! _ = work
                ()
            with
            | ex ->
                MaybeCrash ex
            return ()
        } |> Async.Start

type BalancesPage() as this =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<BalancesPage>)

    let mainLayout = base.FindByName<StackLayout>("mainLayout")
    let totalFiatAmountLabel = mainLayout.FindByName<Label> "totalFiatAmountLabel"
    let totalFiatAmountFrame = mainLayout.FindByName<Frame> "totalFiatAmountFrame"
    let contentLayout = base.FindByName<StackLayout> "contentLayout"


    let normalCryptoBalanceClassId = "normalCryptoBalanceFrame"

    let rec FindCryptoBalances (cryptoBalanceClassId: string) (layout: StackLayout) 
                               (elements: List<View>) (resultsSoFar: List<Frame>): List<Frame> =
        match elements with
        | [] -> resultsSoFar
        | head::tail ->
            match head with
            | :? Frame as frame ->
                let newResults =
                    if frame.ClassId = cryptoBalanceClassId then
                        frame::resultsSoFar
                    else
                        resultsSoFar
                FindCryptoBalances cryptoBalanceClassId layout tail newResults
            | _ ->
                FindCryptoBalances cryptoBalanceClassId layout tail resultsSoFar

    do
        this.Init()

    member this.PopulateGrid ()=
        let activeCurrencyClassId =
            normalCryptoBalanceClassId

        let activeCryptoBalances = FindCryptoBalances activeCurrencyClassId 
                                                      contentLayout 
                                                      (contentLayout.Children |> List.ofSeq) 
                                                      List.Empty

        contentLayout.BatchBegin()

        if activeCryptoBalances.Any() then
            for activeCryptoBalance in activeCryptoBalances do
                activeCryptoBalance.IsVisible <- true
        else
            let tapGestureRecognizer = TapGestureRecognizer()
            tapGestureRecognizer.Tapped.Subscribe(fun _ ->
                let receivePage = ReceivePage()
                NavigationPage.SetHasNavigationBar(receivePage, false)
                let navPage = NavigationPage receivePage

                this.Navigation.PushAsync navPage
                     |> FrontendHelpers.DoubleCheckCompletionNonGeneric
            ) |> ignore


            let stackLayout = StackLayout(Orientation = StackOrientation.Horizontal,
                                          Padding = Thickness(20., 20., 10., 20.))

            let cryptoLabel = Label(Text = "BAR")
            let fiatLabel = Label(Text = "BAR")

            stackLayout.Children.Add cryptoLabel
            stackLayout.Children.Add fiatLabel

            let absoluteLayout = AbsoluteLayout(Margin = Thickness(0., 1., 3., 1.))
            absoluteLayout.Children.Add(stackLayout, Rectangle(0., 0., 1., 1.), AbsoluteLayoutFlags.All)


            let frame = Frame(HasShadow = false,
                              ClassId = activeCurrencyClassId,
                              Content = absoluteLayout,
                              Padding = Thickness(0.),
                              BorderColor = Color.SeaShell)
            frame.GestureRecognizers.Add tapGestureRecognizer

            contentLayout.Children.Add frame

        contentLayout.BatchCommit()


    member private this.Init () =
        Device.BeginInvokeOnMainThread(fun _ ->
            this.PopulateGrid ()

            Device.BeginInvokeOnMainThread(fun _ ->
                totalFiatAmountLabel.Text <- "FOO"
            )
        )
