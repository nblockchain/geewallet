namespace GWallet.Frontend.XF

open System
open System.Threading.Tasks

open Xamarin.Forms
open Xamarin.Forms.Xaml

open Plugin.Clipboard
open ZXing
open ZXing.Net.Mobile.Forms
open ZXing.Common

open GWallet.Backend

type ReceivePage(account: NormalAccount, accountBalance: Label, fiatBalance: Label) as this =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<ReceivePage>)

    let baseAccount = account :> IAccount

    let mainLayout = base.FindByName<StackLayout>("mainLayout")
    do
        this.Init()

    member this.Init() =
        let balanceLabel = mainLayout.FindByName<Label>("balanceLabel")
        balanceLabel.Text <- accountBalance.Text
        balanceLabel.FontSize <- FrontendHelpers.BigFontSize
        let fiatBalanceLabel = mainLayout.FindByName<Label>("fiatBalanceLabel")
        fiatBalanceLabel.Text <- fiatBalance.Text
        fiatBalanceLabel.FontSize <- FrontendHelpers.MediumFontSize

        // FIXME: pass the decimal instead of doing the HACK below:
        if not (accountBalance.Text.StartsWith "0 ") then
            mainLayout.FindByName<Button>("sendButton").IsEnabled <- true

        // TODO: add a "List transactions" button using Device.OpenUri() with etherscan, gastracker, etc

        let size = 200
        let encodingOptions = EncodingOptions(Height = size,
                                              Width = size)
        let barCode = ZXingBarcodeImageView(HorizontalOptions = LayoutOptions.Center,
                                            VerticalOptions = LayoutOptions.Center,
                                            BarcodeFormat = BarcodeFormat.QR_CODE,
                                            BarcodeValue = baseAccount.PublicAddress,
                                            HeightRequest = float size,
                                            WidthRequest = float size,
                                            BarcodeOptions = encodingOptions)
        mainLayout.Children.Add(barCode)

        let transactionHistoryButton = Button(Text = "View transaction history...")
        transactionHistoryButton.Clicked.Subscribe(fun _ ->
            Device.OpenUri (BlockExplorer.GetTransactionHistory baseAccount)
        ) |> ignore
        mainLayout.Children.Add(transactionHistoryButton)

        let backButton = Button(Text = "< Go back")
        backButton.Clicked.Subscribe(fun _ ->
            this.Navigation.PopModalAsync() |> FrontendHelpers.DoubleCheckCompletion
        ) |> ignore
        mainLayout.Children.Add(backButton)
        ()

    member this.OnSendPaymentClicked(sender: Object, args: EventArgs) =
        this.Navigation.PushModalAsync(SendPage(account))
            |> FrontendHelpers.DoubleCheckCompletion
        ()

    member this.OnCopyToClipboardClicked(sender: Object, args: EventArgs) =
        let copyToClipboardButton = base.FindByName<Button>("copyToClipboardButton")
        FrontendHelpers.ChangeTextAndChangeBack copyToClipboardButton "Copied"

        CrossClipboard.Current.SetText baseAccount.PublicAddress
        ()
