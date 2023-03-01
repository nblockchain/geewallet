#if !XAMARIN
namespace GWallet.Frontend.Maui
#else
namespace GWallet.Frontend.XF
#endif

open System
open System.Threading.Tasks

#if !XAMARIN
open Microsoft.Maui.Controls
open Microsoft.Maui.Controls.Xaml
open Microsoft.Maui.ApplicationModel
#else
open Xamarin.Forms
open Xamarin.Forms.Xaml
open Xamarin.Essentials
#endif

open GWallet.Backend

#if !XAMARIN
// state and masterPrivateKeyGenerationTask are unused for now in MAUI until LoadingPage is ported
type WelcomePage2(_state: FrontendHelpers.IGlobalAppState, _masterPrivateKeyGenerationTask: Task<array<byte>>) =
#else
type WelcomePage2(state: FrontendHelpers.IGlobalAppState, masterPrivateKeyGenerationTask: Task<array<byte>>) =
#endif
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<WelcomePage2>)

    let mainLayout = base.FindByName<Grid> "mainLayout"

    let password = mainLayout.FindByName<Entry> "passwordEntry"
    let passwordConfirmation = mainLayout.FindByName<Entry> "passwordEntryConfirmation"
    let finishButton = mainLayout.FindByName<Button> "finishButton"

    let MaybeEnableFinishButton() =

        if password.Text <> null && password.Text.Length > 0 &&
           passwordConfirmation.Text <> null && passwordConfirmation.Text.Length > 0 then
            finishButton.IsEnabled <- true

    let ToggleInputWidgetsEnabledOrDisabled (enabled: bool) =

        let newCreateButtonCaption =
            if enabled then
                "Finish"
            else
                "Finishing..."

        MainThread.BeginInvokeOnMainThread(fun _ ->
            password.IsEnabled <- enabled
            passwordConfirmation.IsEnabled <- enabled
            finishButton.IsEnabled <- enabled
            finishButton.Text <- newCreateButtonCaption
        )

    [<Obsolete(DummyPageConstructorHelper.Warning)>]
    new() = WelcomePage2(DummyPageConstructorHelper.GlobalFuncToRaiseExceptionIfUsedAtRuntime(),
                         new Task<array<byte>>(fun _ -> null))

    member self.OnFinishButtonClicked(_sender: Object, _args: EventArgs) =
        if password.Text <> passwordConfirmation.Text then
            self.DisplayAlert("Alert", "Payment passwords don't match, please try again", "OK") |> ignore
        else

            ToggleInputWidgetsEnabledOrDisabled false

            async {
#if XAMARIN                  
                let! privateKeyBytes = Async.AwaitTask masterPrivateKeyGenerationTask
                do! Account.CreateAllAccounts privateKeyBytes password.Text
                let loadingPage () =
                    LoadingPage (state, false)
                        :> Page
                FrontendHelpers.SwitchToNewPageDiscardingCurrentOne self loadingPage
#else        
                ()
#endif                
            } |> FrontendHelpers.DoubleCheckCompletionAsync false

    member __.OnPasswordTextChanged(_sender: Object, _args: EventArgs) =
        MaybeEnableFinishButton()

    