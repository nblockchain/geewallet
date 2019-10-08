namespace GWallet.Frontend.XF

open System
open System.Threading.Tasks

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Backend

type WelcomePage2(state: FrontendHelpers.IGlobalAppState, masterPrivateKeyGenerationTask: Task<array<byte>>) =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<WelcomePage2>)

    let mainLayout = base.FindByName<StackLayout> "mainLayout"

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

        Device.BeginInvokeOnMainThread(fun _ ->
            password.IsEnabled <- enabled
            passwordConfirmation.IsEnabled <- enabled
            finishButton.IsEnabled <- enabled
            finishButton.Text <- newCreateButtonCaption
        )

    [<Obsolete(DummyPageConstructorHelper.Warning)>]
    new() = WelcomePage2(DummyPageConstructorHelper.GlobalFuncToRaiseExceptionIfUsedAtRuntime(),
                         new Task<array<byte>>(fun _ -> null))

    member this.OnFinishButtonClicked(sender: Object, args: EventArgs) =
        if password.Text <> passwordConfirmation.Text then
            this.DisplayAlert("Alert", "Payment passwords don't match, please try again", "OK") |> ignore
        else

            ToggleInputWidgetsEnabledOrDisabled false

            async {
                do! Account.CreateAllAccounts masterPrivateKeyGenerationTask password.Text
                Device.BeginInvokeOnMainThread(fun _ ->
                    FrontendHelpers.SwitchToNewPageDiscardingCurrentOne this (LoadingPage (state, false))
                )
            } |> FrontendHelpers.DoubleCheckCompletionAsync false

    member this.OnPasswordTextChanged(sender: Object, args: EventArgs) =
        MaybeEnableFinishButton()

    