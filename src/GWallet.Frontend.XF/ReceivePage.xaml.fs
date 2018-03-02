namespace GWallet.Frontend.XF

open System

open Xamarin.Forms
open Xamarin.Forms.Xaml

open Plugin.Clipboard
open ZXing
open ZXing.Net.Mobile.Forms
open ZXing.Common

open GWallet.Backend

type ReceivePage(account: NormalAccount) as this =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<ReceivePage>)

    let acc = account :> IAccount

    let mainLayout = base.FindByName<StackLayout>("mainLayout")
    do
        this.Init()


    member this.Init() =
        let titleLabel = base.FindByName<Label>("receiveTitleLabel")
        titleLabel.Text <- "Receive " + acc.Currency.ToString()
        let size = 200
        let encodingOptions = EncodingOptions(Height = size,
                                              Width = size)
        let barCode = ZXingBarcodeImageView(HorizontalOptions = LayoutOptions.Center,
                                            VerticalOptions = LayoutOptions.Center,
                                            BarcodeFormat = BarcodeFormat.QR_CODE,
                                            BarcodeValue = acc.PublicAddress,
                                            HeightRequest = float size,
                                            WidthRequest = float size,
                                            BarcodeOptions = encodingOptions)
        mainLayout.Children.Add(barCode)

        let backButton = Button(Text = "< Go back")
        backButton.Clicked.Subscribe(fun _ ->
            this.Navigation.PopModalAsync() |> FrontendHelpers.DoubleCheckCompletion
        ) |> ignore
        mainLayout.Children.Add(backButton)
        ()

    member this.OnCopyToClipboardClicked(sender: Object, args: EventArgs) =
        CrossClipboard.Current.SetText (account :> IAccount).PublicAddress
        ()
