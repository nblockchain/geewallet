namespace GWallet.Frontend.XF

open System
open System.Threading.Tasks

open Xamarin.Forms

module FrontendHelpers =

    // these days cryptos are not so volatile, so 30mins should be good...
    let internal TimeSpanToConsiderExchangeRateOutdated = TimeSpan.FromMinutes 30.0

    let private MaybeCrash (canBeCanceled: bool) (ex: Exception) =
        let LastResortBail() =
            Device.PlatformServices.QuitApplication()

        if null = ex then
            ()
        else
            Device.BeginInvokeOnMainThread(fun _ ->
                raise ex
                LastResortBail()
            )
            raise ex
            LastResortBail()

    // when running Task<unit> or Task<T> where we want to ignore the T, we should still make sure there is no exception,
    // & if there is, bring it to the main thread to fail fast, report to Sentry, etc, otherwise it gets ignored
    let DoubleCheckCompletion<'T> (task: Task<'T>) =
        task.ContinueWith(fun (t: Task<'T>) ->
            MaybeCrash false t.Exception
        , TaskContinuationOptions.OnlyOnFaulted) |> ignore
    let DoubleCheckCompletionNonGeneric (task: Task) =
        task.ContinueWith(fun (t: Task) ->
            MaybeCrash false t.Exception
        , TaskContinuationOptions.OnlyOnFaulted) |> ignore

    let DoubleCheckCompletionAsync<'T> (canBeCanceled: bool) (work: Async<'T>): unit =
        async {
            try
                let! _ = work
                ()
            with
            | ex ->
                MaybeCrash canBeCanceled ex
            return ()
        } |> Async.Start

    let SwitchToNewPage (currentPage: Page) (createNewPage: unit -> Page) (navBar: bool): unit =
        Device.BeginInvokeOnMainThread(fun _ ->
            let newPage = createNewPage ()
            NavigationPage.SetHasNavigationBar(newPage, false)
            let navPage = NavigationPage newPage
            NavigationPage.SetHasNavigationBar(navPage, navBar)

            currentPage.Navigation.PushAsync navPage
                |> DoubleCheckCompletionNonGeneric
        )

    let SwitchToNewPageDiscardingCurrentOne (currentPage: Page) (createNewPage: unit -> Page): unit =
        Device.BeginInvokeOnMainThread(fun _ ->
            let newPage = createNewPage ()
            NavigationPage.SetHasNavigationBar(newPage, false)

            currentPage.Navigation.InsertPageBefore(newPage, currentPage)
            currentPage.Navigation.PopAsync()
                |> DoubleCheckCompletion
        )

    let SwitchToNewPageDiscardingCurrentOneAsync (currentPage: Page) (createNewPage: unit -> Page) =
        async {
            let newPage = createNewPage ()
            NavigationPage.SetHasNavigationBar(newPage, false)

            currentPage.Navigation.InsertPageBefore(newPage, currentPage)
            let! _ =
                currentPage.Navigation.PopAsync()
                |> Async.AwaitTask
            return ()
        }

    let ChangeTextAndChangeBack (button: Button) (newText: string) =
        let initialText = button.Text
        button.IsEnabled <- false
        button.Text <- newText
        Task.Run(fun _ ->
            Task.Delay(TimeSpan.FromSeconds(2.0)).Wait()
            Device.BeginInvokeOnMainThread(fun _ ->
                button.Text <- initialText
                button.IsEnabled <- true
            )
        ) |> DoubleCheckCompletionNonGeneric
