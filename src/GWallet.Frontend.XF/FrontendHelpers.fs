namespace GWallet.Frontend.XF

open System
open System.Threading.Tasks

open Xamarin.Forms

module FrontendHelpers =

    let private MaybeCrash (ex: Exception) =
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
    let DoubleCheckCompletionNonGeneric (task: Task) =
        task.ContinueWith(
            (fun (t: Task) -> MaybeCrash t.Exception),
            TaskContinuationOptions.OnlyOnFaulted
        )
        |> ignore

    let SwitchToNewPage (currentPage: Page) (createNewPage: unit -> Page) (navBar: bool): unit =
        Device.BeginInvokeOnMainThread(fun _ ->
            let newPage = createNewPage ()
            NavigationPage.SetHasNavigationBar(newPage, false)
            let navPage = NavigationPage newPage
            NavigationPage.SetHasNavigationBar(navPage, navBar)

            currentPage.Navigation.PushAsync navPage
                |> DoubleCheckCompletionNonGeneric
        )
