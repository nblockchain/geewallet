namespace GWallet.Frontend.XF

open System
open System.Linq

open Xamarin.Forms
open Xamarin.Forms.Xaml
open Fsdk

open GWallet.Backend

type SettingsPage(option: string) as this =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<SettingsPage>)
    let titleLabel = base.FindByName<Label> "titleLabel"

    //check password
    let passwordEntry = base.FindByName<Entry> "passwordEntry"

    //check passphrase
    let phraseEntry = base.FindByName<Entry> "phraseEntry"
    let emailEntry = base.FindByName<Entry> "emailEntry"
    let dateOfBirthPicker = base.FindByName<DatePicker> "dateOfBirthPicker"
    let dateOfBirthLabel = base.FindByName<Entry> "dateOfBirthLabel"

    //loading & result
    let resultMessage = base.FindByName<Label> "resultMessage"
    let loadingIndicator = base.FindByName<ActivityIndicator> "loadingIndicator"

    do
        this.Init()
                    
    member self.Init () =
        titleLabel.Text <- option

    member self.OnCheckPasswordButtonClicked(_sender: Object, _args: EventArgs) =
        this.PerformCheck (Account.CheckValidPassword passwordEntry.Text None)

    member self.OnCheckSeedPassphraseClicked(_sender: Object, _args: EventArgs) =
        this.PerformCheck (Account.CheckValidSeed phraseEntry.Text dateOfBirthPicker.Date emailEntry.Text)
        
    member self.PerformCheck(checkTask: Async<bool>) =
        async {
            Device.BeginInvokeOnMainThread(
                fun () ->
                    loadingIndicator.IsVisible <- true
                    resultMessage.IsVisible <- false)
            let! checkResult = checkTask
            Device.BeginInvokeOnMainThread(
                fun () ->
                    loadingIndicator.IsVisible <- false
                    resultMessage.IsVisible <- true
                    resultMessage.Text <- if checkResult then "Success!" else "Try again" )
        } |> FrontendHelpers.DoubleCheckCompletionAsync true

    member self.OnWipeWalletButtonClicked (_sender: Object, _args: EventArgs) =
        async {
           let! result = Application.Current.MainPage.DisplayAlert("Are you sure?", "Are you ABSOLUTELY SURE about this?", "Yes", "No") |> Async.AwaitTask
           if result then
                Account.WipeAll()
                let displayTask = Application.Current.MainPage.DisplayAlert("Success", "You successfully wiped your current wallet", "Ok")
                do! Async.AwaitTask displayTask

        } |> Async.StartImmediate

    member self.OnDatePickerDateSelected (_sender: obj) (evArgs: DateChangedEventArgs) =
        dateOfBirthLabel.Text <- evArgs.NewDate.ToString("dd MMMM yyyy")

    member self.ClearResult (_sender: Object, _args: EventArgs) =
        resultMessage.IsVisible <- false
