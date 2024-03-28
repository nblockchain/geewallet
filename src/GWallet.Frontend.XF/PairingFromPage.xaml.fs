namespace GWallet.Frontend.XF

open System

open Xamarin.Forms
open Xamarin.Forms.Xaml
open Xamarin.Essentials

open ZXing.Net.Mobile.Forms

type PairingFromPage(previousPage: Page,
                     clipBoardButtonCaption: string,
                     qrCodeContents: string,
                     nextButtonCaptionAndSendPage: Option<string*FrontendHelpers.IAugmentablePayPage>) as self =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<PairingFromPage>)

    let mainLayout = base.FindByName<StackLayout>("mainLayout")
    do
        self.Init()

    [<Obsolete(DummyPageConstructorHelper.Warning)>]
    new() = PairingFromPage(DummyPageConstructorHelper.PageFuncToRaiseExceptionIfUsedAtRuntime(),
                            String.Empty,String.Empty,None)

    member __.Init() =

        let clipBoardButton = mainLayout.FindByName<Button> "copyToClipboardButton"
        clipBoardButton.Text <- clipBoardButtonCaption

        let qrCode = mainLayout.FindByName<ZXingBarcodeImageView> "qrCode"
        if isNull qrCode then
            failwith "Couldn't find QR code"
        qrCode.BarcodeValue <- qrCodeContents
        qrCode.IsVisible <- true
        qrCode.BarcodeFormat <- ZXing.BarcodeFormat.QR_CODE
        qrCode.BarcodeOptions <- ZXing.Common.EncodingOptions(Width = 400, Height = 400)

        let nextStepButton = mainLayout.FindByName<Button> "nextStepButton"
        match nextButtonCaptionAndSendPage with
        | Some (caption,_) ->
            nextStepButton.Text <- caption
            nextStepButton.IsVisible <- true
        | None -> ()

        // FIXME: remove this workaround below when https://github.com/xamarin/Xamarin.Forms/issues/8843 gets fixed
        // TODO: file the UWP bug too
        if Device.RuntimePlatform <> Device.macOS && Device.RuntimePlatform <> Device.UWP then () else

        let backButton = Button(Text = "< Go back")
        backButton.Clicked.Subscribe(fun _ ->
            MainThread.BeginInvokeOnMainThread(fun _ ->
                previousPage.Navigation.PopAsync() |> FrontendHelpers.DoubleCheckCompletion
            )
        ) |> ignore
        mainLayout.Children.Add(backButton)
        //</workaround> (NOTE: this also exists in ReceivePage.xaml.fs)

    member __.OnCopyToClipboardClicked(_sender: Object, _args: EventArgs) =
        let copyToClipboardButton = base.FindByName<Button>("copyToClipboardButton")
        FrontendHelpers.ChangeTextAndChangeBack copyToClipboardButton "Copied"

        Clipboard.SetTextAsync qrCodeContents
            |> FrontendHelpers.DoubleCheckCompletionNonGeneric

    member __.OnNextStepClicked(_sender: Object, _args: EventArgs) =
        match nextButtonCaptionAndSendPage with
        | None ->
            failwith "if next step clicked, last param in ctor should have been Some"
        | Some (_, sendPage) ->
            MainThread.BeginInvokeOnMainThread(fun _ ->
                let popTask = previousPage.Navigation.PopAsync()
                sendPage.AddTransactionScanner()
                popTask |> FrontendHelpers.DoubleCheckCompletionNonGeneric
            )
        ()
