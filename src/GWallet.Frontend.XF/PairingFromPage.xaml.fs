namespace GWallet.Frontend.XF

open System

open Xamarin.Forms
open Xamarin.Forms.Xaml
open Xamarin.Essentials

open ZXing.Net.Mobile.Forms

type PairingFromPage(previousPage: Page,
                     clipBoardButtonCaption: string,
                     qrCodeContents: string,
                     nextButtonCaptionAndSendPage: Option<string*FrontendHelpers.IAugmentablePayPage>) as this =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<PairingFromPage>)

    let mainLayout = base.FindByName<StackLayout>("mainLayout")
    do
        this.Init()

    [<Obsolete(DummyPageConstructorHelper.Warning)>]
    new() = PairingFromPage(DummyPageConstructorHelper.PageFuncToRaiseExceptionIfUsedAtRuntime(),
                            String.Empty,String.Empty,None)

    member this.Init() =

        let clipBoardButton = mainLayout.FindByName<Button> "copyToClipboardButton"
        clipBoardButton.Text <- clipBoardButtonCaption

        let qrCode = mainLayout.FindByName<ZXingBarcodeImageView> "qrCode"
        if (qrCode = null) then
            failwith "Couldn't find QR code"
        qrCode.BarcodeValue <- qrCodeContents
        qrCode.IsVisible <- true

        let nextStepButton = mainLayout.FindByName<Button> "nextStepButton"
        match nextButtonCaptionAndSendPage with
        | Some (caption,_) ->
            nextStepButton.Text <- caption
            nextStepButton.IsVisible <- true
        | None -> ()

        // FIXME: report this Xamarin.Forms Mac backend bug (no back button in navigation pages!, so below <workaround>)
        if (Device.RuntimePlatform <> Device.macOS) then () else

        let backButton = Button(Text = "< Go back")
        backButton.Clicked.Subscribe(fun _ ->
            Device.BeginInvokeOnMainThread(fun _ ->
                previousPage.Navigation.PopAsync() |> FrontendHelpers.DoubleCheckCompletion
            )
        ) |> ignore
        mainLayout.Children.Add(backButton)
        //</workaround>

    member this.OnCopyToClipboardClicked(sender: Object, args: EventArgs) =
        let copyToClipboardButton = base.FindByName<Button>("copyToClipboardButton")
        FrontendHelpers.ChangeTextAndChangeBack copyToClipboardButton "Copied"

        Clipboard.SetTextAsync qrCodeContents
            |> FrontendHelpers.DoubleCheckCompletionNonGeneric

    member this.OnNextStepClicked(sender: Object, args: EventArgs) =
        match nextButtonCaptionAndSendPage with
        | None ->
            failwith "if next step clicked, last param in ctor should have been Some"
        | Some (_, sendPage) ->
            Device.BeginInvokeOnMainThread(fun _ ->
                let popTask = previousPage.Navigation.PopAsync()
                sendPage.AddTransactionScanner()
                popTask |> FrontendHelpers.DoubleCheckCompletionNonGeneric
            )
        ()
