namespace GWallet.Frontend.XF

open System
open System.Linq
open System.Globalization

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Backend

type WelcomePage() =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<WelcomePage>)

    let mainLayout = base.FindByName<StackLayout>("mainLayout")
    let passphrase = mainLayout.FindByName<Entry>("passphraseEntry")
    let passphraseConfirmation = mainLayout.FindByName<Entry>("passphraseEntryConfirmation")

    let email = mainLayout.FindByName<Entry>("emailEntry")
    let dob = mainLayout.FindByName<Entry>("dobEntry")

    let password = mainLayout.FindByName<Entry>("passwordEntry")
    let passwordConfirmation = mainLayout.FindByName<Entry>("passwordEntryConfirmation")

    let MaybeEnableCreateButton() =
        let createButton = mainLayout.FindByName<Button>("createButton")
        if (passphrase.Text <> null && passphrase.Text.Length > 0 &&
            passphraseConfirmation.Text <> null && passphraseConfirmation.Text.Length > 0 &&
            password.Text <> null && password.Text.Length > 0 &&
            passwordConfirmation.Text <> null && passwordConfirmation.Text.Length > 0 &&
            email.Text <> null && email.Text.Length > 0 &&
            dob.Text <> null && dob.Text.Length > 0) then
            createButton.IsEnabled <- true

    let LENGTH_OF_CURRENT_UNSOLVED_WARPWALLET_CHALLENGE = 8

    let VerifyPassphraseIsGoodAndSecureEnough(): Option<string> =
        let containsASpaceAtLeast = passphrase.Text.Contains " "
        let containsADigitAtLeast = passphrase.Text.Any(fun c -> Char.IsDigit c)
        let containsPunctuation = passphrase.Text.Any(fun c -> c = ',' ||
                                                               c = ';' ||
                                                               c = '.' ||
                                                               c = '?' ||
                                                               c = '!' ||
                                                               c = ''')

        if (passphrase.Text <> passphraseConfirmation.Text) then
            Some "Seed passphrases don't match, please try again"
        elif (passphrase.Text.Length < LENGTH_OF_CURRENT_UNSOLVED_WARPWALLET_CHALLENGE) then
            Some (sprintf "Seed passphrases should contain %d or more characters, for security reasons"
                          LENGTH_OF_CURRENT_UNSOLVED_WARPWALLET_CHALLENGE)
        elif (not containsASpaceAtLeast) && (not containsADigitAtLeast) then
            Some "If your passphrase doesn't contain spaces (thus only a password), at least use numbers too"
        elif (passphrase.Text.ToLower() = passphrase.Text || passphrase.Text.ToUpper() = passphrase.Text) then
            Some "Mix lowercase and uppercase characters in your seed phrase please"
        elif (containsASpaceAtLeast && (not containsADigitAtLeast) && (not containsPunctuation)) then
            Some "For security reasons, please include numbers or punctuation in your passphrase (to increase entropy)"
        else
            None

    member this.OnCreateButtonClicked(sender: Object, args: EventArgs) =
        match VerifyPassphraseIsGoodAndSecureEnough() with
        | Some warning ->
            this.DisplayAlert("Alert", warning, "OK") |> ignore

        | None ->

            if (password.Text <> passwordConfirmation.Text) then
                this.DisplayAlert("Alert", "Payment passwords don't match, please try again", "OK") |> ignore
            else
                let dateFormat = "dd/MM/yyyy"
                match DateTime.TryParseExact(dob.Text, dateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None) with
                | false,_ ->
                    this.DisplayAlert("Alert", sprintf "Invalid date or invalid date format (%s)" dateFormat, "OK") |> ignore

                | true,dateTime ->

                    let createButton = mainLayout.FindByName<Button>("createButton")
                    createButton.IsEnabled <- false
                    createButton.Text <- "Creating..."

                    async {
                        let! accounts = Account.CreateBaseAccount passphrase.Text dateTime (email.Text.ToLower()) password.Text
                        Device.BeginInvokeOnMainThread(fun _ ->
                            let balancesPage = BalancesPage()
                            NavigationPage.SetHasNavigationBar(balancesPage, false)
                            this.Navigation.InsertPageBefore(balancesPage, this)
                            this.Navigation.PopAsync() |> FrontendHelpers.DoubleCheckCompletion
                        )
                    } |> FrontendHelpers.DoubleCheckCompletion

    member this.OnDobTextChanged(sender: Object, args: EventArgs) =
        MaybeEnableCreateButton()

    member this.OnEmailTextChanged(sender: Object, args: EventArgs) =
        MaybeEnableCreateButton()

    member this.OnPasswordTextChanged(sender: Object, args: EventArgs) =
        MaybeEnableCreateButton()

    member this.OnPassphraseTextChanged(sender: Object, args: EventArgs) =
        MaybeEnableCreateButton()

