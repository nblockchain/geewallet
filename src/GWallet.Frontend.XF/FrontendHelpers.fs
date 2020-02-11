namespace GWallet.Frontend.XF

open System
open System.Threading.Tasks

open Xamarin.Forms

module FrontendHelpers =

    let private MaybeCrash (canBeCanceled: bool) (ex: Exception) =
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

    let SwitchToNewPage (currentPage: Page) (newPage: Page) (navBar: bool): unit =
        NavigationPage.SetHasNavigationBar(newPage, navBar)
        let navPage = NavigationPage newPage
        NavigationPage.SetHasNavigationBar(navPage, navBar)

        Device.BeginInvokeOnMainThread(fun _ ->
            currentPage.Navigation.PushAsync navPage
                |> DoubleCheckCompletionNonGeneric
        )


