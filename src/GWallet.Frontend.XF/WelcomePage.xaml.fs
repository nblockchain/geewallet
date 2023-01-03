namespace GWallet.Frontend.XF

open System
open System.Linq

open Xamarin.Forms
open Xamarin.Forms.Xaml
open Xamarin.Essentials

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks

type WelcomePage(state: FrontendHelpers.IGlobalAppState) =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<WelcomePage>)

    let mainLayout = base.FindByName<StackLayout> "mainLayout"
    let infoGrid = mainLayout.FindByName<Grid> "infoGrid"
    let passphraseEntry = mainLayout.FindByName<Entry> "passphraseEntry"
    let passphraseConfirmationEntry = mainLayout.FindByName<Entry> "passphraseConfirmationEntry"

    let emailEntry = mainLayout.FindByName<Entry> "emailEntry"
    let dobDatePicker = mainLayout.FindByName<DatePicker> "dobDatePicker"
    let nextButton = mainLayout.FindByName<Button> "nextButton"

    let eighteenYearsAgo = (DateTime.Now - TimeSpan.FromDays (365.25 * 18.)).Year
    let middleDateEighteenYearsAgo = DateTime(eighteenYearsAgo, 6, 15)

    let MaybeEnableNextButton () =
        let isEnabled = passphraseEntry.Text <> null && passphraseEntry.Text.Length > 0 &&
                        passphraseConfirmationEntry.Text <> null && passphraseConfirmationEntry.Text.Length > 0 &&
                        emailEntry.Text <> null && emailEntry.Text.Length > 0 &&
                        dobDatePicker.Date >= dobDatePicker.MinimumDate && dobDatePicker.Date < dobDatePicker.MaximumDate

        nextButton.IsEnabled <- isEnabled

    let LENGTH_OF_CURRENT_UNSOLVED_WARPWALLET_CHALLENGE = 8

    let VerifyPassphraseIsGoodAndSecureEnough(): Option<string> =
        let containsASpaceAtLeast = passphraseEntry.Text.Contains " "
        let containsADigitAtLeast = passphraseEntry.Text.Any(fun c -> Char.IsDigit c)
        let containsPunctuation = passphraseEntry.Text.Any(fun c -> c = ',' ||
                                                                    c = ';' ||
                                                                    c = '.' ||
                                                                    c = '?' ||
                                                                    c = '!' ||
                                                                    c = ''')
        let IsColdStorageMode() =
            let currentConnectivityInstance = Connectivity.NetworkAccess
            currentConnectivityInstance <> NetworkAccess.Internet

        let AllWordsInPassphraseExistInDictionaries(passphrase: string): bool =
            let words = passphrase.Split([|","; "."; " "; "-"; "_"|], StringSplitOptions.RemoveEmptyEntries)
            let result: bool = words

                               |> Seq.map (fun word -> word.ToLower())

                               // TODO: instead of using NBitcoin, we should use a proper dictionary library
                               //       (i.e. which might have more languages and more complete dictionaries)
                               |> Seq.forall(fun word -> not ((NBitcoin.Wordlist.AutoDetectLanguage word)
                                                                 .Equals NBitcoin.Language.Unknown)
                                            )
            result

        if passphraseEntry.Text <> passphraseConfirmationEntry.Text then
            Some "Secret recovery phrases don't match, please try again"
        elif passphraseEntry.Text.Length < LENGTH_OF_CURRENT_UNSOLVED_WARPWALLET_CHALLENGE then
            Some (SPrintF1 "Secret recovery phrases should contain %i or more characters, for security reasons"
                          LENGTH_OF_CURRENT_UNSOLVED_WARPWALLET_CHALLENGE)
        elif (not containsASpaceAtLeast) && (not containsADigitAtLeast) then
            Some "If your secret recovery phrase doesn't contain spaces (thus only a single password), at least use numbers too"
        elif passphraseEntry.Text.ToLower() = passphraseEntry.Text || passphraseEntry.Text.ToUpper() = passphraseEntry.Text then
            Some "Mix lowercase and uppercase characters in your secret recovery phrase please"
        elif containsASpaceAtLeast && (not containsADigitAtLeast) && (not containsPunctuation) then
            Some "For security reasons, please include numbers or punctuation in your secret recovery phrase (to increase entropy)"
        elif IsColdStorageMode() && AllWordsInPassphraseExistInDictionaries passphraseEntry.Text then
            Some "For security reasons, please include at least one word that does not exist in any dictionary (to increase entropy) in your secret recovery phrase"
        else
            None

    let ToggleInputWidgetsEnabledOrDisabled (enabled: bool) =
        let newCreateButtonCaption =
            if enabled then
                "Next"
            else
                "Loading..."

        Device.BeginInvokeOnMainThread(fun _ ->
            passphraseEntry.IsEnabled <- enabled
            passphraseConfirmationEntry.IsEnabled <- enabled
            dobDatePicker.IsEnabled <- enabled
            emailEntry.IsEnabled <- enabled
            nextButton.IsEnabled <- enabled
            nextButton.Text <- newCreateButtonCaption
        )

    do
        dobDatePicker.MaximumDate <- DateTime.UtcNow.Date
        dobDatePicker.Date <- middleDateEighteenYearsAgo

        Caching.Instance.BootstrapServerStatsFromTrustedSource()
            |> FrontendHelpers.DoubleCheckCompletionAsync false

    [<Obsolete(DummyPageConstructorHelper.Warning)>]
    new() = WelcomePage(DummyPageConstructorHelper.GlobalFuncToRaiseExceptionIfUsedAtRuntime())

    member this.OnNextButtonClicked(_sender: Object, _args: EventArgs) =
        let submit () =
            match VerifyPassphraseIsGoodAndSecureEnough() with
            | Some warning ->
                async {
                    do!
                        (fun () ->
                            this.DisplayAlert("Alert", warning, "OK")
                        )
                        |> Device.InvokeOnMainThreadAsync
                        |> Async.AwaitTask
                }
            | None ->
                async {
                    let! mainThreadSynchContext =
                        Async.AwaitTask <| Device.GetMainThreadSynchronizationContextAsync()
                    do! Async.SwitchToContext mainThreadSynchContext
                    let dateTime = dobDatePicker.Date
                    ToggleInputWidgetsEnabledOrDisabled false
                    do! Async.SwitchToThreadPool()
                    let masterPrivKeyTask =
                        Account.GenerateMasterPrivateKey passphraseEntry.Text dateTime (emailEntry.Text.ToLower())
                            |> Async.StartAsTask
                    do! Async.SwitchToContext mainThreadSynchContext
                    let welcomePage () =
                        WelcomePage2 (state, masterPrivKeyTask)
                            :> Page
                    do! FrontendHelpers.SwitchToNewPageDiscardingCurrentOneAsync this welcomePage
                }

        if dobDatePicker.Date.Date = middleDateEighteenYearsAgo.Date then
            let displayTask =
                this.DisplayAlert("Alert", "The field for Date of Birth has not been set, are you sure?", "Yes, the date is correct", "Cancel")
            async {
                let! mainThreadSynchContext =
                    Async.AwaitTask <| Device.GetMainThreadSynchronizationContextAsync()
                do! Async.SwitchToContext mainThreadSynchContext
                let! continueAnyway = Async.AwaitTask displayTask
                if continueAnyway then
                    do! submit()
                    return ()
                else
                    return ()
            } |> FrontendHelpers.DoubleCheckCompletionAsync false
        else
            submit () |> FrontendHelpers.DoubleCheckCompletionAsync false

    member private this.DisplayInfo() =
        this.DisplayAlert("Info", "Please note that geewallet is a brain-wallet, which means that this personal information is not registered in any server or any location outside your device, not even saved in your device. It will just be combined and hashed to generate a unique secret which is called a 'private key' which will allow you to recover your funds if you install the application again (in this or other device) later. \r\n\r\n(If it is your first time using this wallet and just want to test it quickly without any funds or low amounts, you can just input any data that is long enough to be considered valid.)", "OK")

    member this.OnMoreInfoButtonClicked(_sender: Object, _args: EventArgs) =
        this.DisplayInfo
        |> Device.InvokeOnMainThreadAsync
        |> FrontendHelpers.DoubleCheckCompletionNonGeneric

    member __.OnOkButtonClicked(_sender: Object, _args: EventArgs) =
        infoGrid.IsVisible <- false
        if isNull passphraseEntry.Text || passphraseEntry.Text.Trim().Length = 0 then
            passphraseEntry.Focus() |> ignore

    member this.OnDobDateChanged (_sender: Object, _args: DateChangedEventArgs) =
        MaybeEnableNextButton ()

    member this.OnEmailTextChanged(_sender: Object, _args: EventArgs) =
        MaybeEnableNextButton ()

    member this.OnPassphraseTextChanged(_sender: Object, _args: EventArgs) =
        MaybeEnableNextButton ()

