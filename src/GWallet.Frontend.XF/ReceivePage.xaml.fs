namespace GWallet.Frontend.XF

open System

open Xamarin.Forms
open Xamarin.Forms.Xaml
open Xamarin.Essentials
open ZXing
open ZXing.Net.Mobile.Forms
open ZXing.Common

open GWallet.Backend

type ReceivePage(account: IAccount,
                 balancesPage: Page,
                 balanceWidgetsFromBalancePage: BalanceWidgets) as this =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<ReceivePage>)

    let mainLayout = base.FindByName<StackLayout>("mainLayout")
    let paymentButton = mainLayout.FindByName<Button> "paymentButton"

    let paymentCaption = "Send Payment"
    let paymentCaptionInColdStorage = "Signoff Payment Offline"

    do
        this.Init()

    [<Obsolete(DummyPageConstructorHelper.Warning)>]
    new() = ReceivePage(ReadOnlyAccount(Currency.BTC, { Name = "dummy"; Content = fun _ -> "" }, fun _ -> ""),
                        DummyPageConstructorHelper.PageFuncToRaiseExceptionIfUsedAtRuntime(),
                        { CryptoLabel = null; FiatLabel = null ; Frame = null })

    member this.Init() =
        let balanceLabel = mainLayout.FindByName<Label>("balanceLabel")
        let fiatBalanceLabel = mainLayout.FindByName<Label>("fiatBalanceLabel")

        let accountBalance =
            Caching.Instance.RetrieveLastCompoundBalance account.PublicAddress account.Currency
        FrontendHelpers.UpdateBalance (NotFresh accountBalance) account.Currency None balanceLabel fiatBalanceLabel
            |> ignore

        // this below is for the case when a new ReceivePage() instance is suddenly created after sending a transaction
        // (we need to update the balance page ASAP in case the user goes back to it after sending the transaction)
        FrontendHelpers.UpdateBalance (NotFresh accountBalance)
                                      account.Currency
                                      (Some balanceWidgetsFromBalancePage.Frame)
                                      balanceWidgetsFromBalancePage.CryptoLabel
                                      balanceWidgetsFromBalancePage.FiatLabel
            |> ignore

        balanceLabel.FontSize <- FrontendHelpers.BigFontSize
        fiatBalanceLabel.FontSize <- FrontendHelpers.MediumFontSize

        let size = 200
        let encodingOptions = EncodingOptions(Height = size,
                                              Width = size)
        let barCode = ZXingBarcodeImageView(HorizontalOptions = LayoutOptions.Center,
                                            VerticalOptions = LayoutOptions.Center,
                                            BarcodeFormat = BarcodeFormat.QR_CODE,
                                            BarcodeValue = account.PublicAddress,
                                            HeightRequest = float size,
                                            WidthRequest = float size,
                                            BarcodeOptions = encodingOptions)
        mainLayout.Children.Add(barCode)

        let transactionHistoryButton = Button(Text = "View transaction history...")
        transactionHistoryButton.Clicked.Subscribe(fun _ ->
            BlockExplorer.GetTransactionHistory account
                |> Xamarin.Essentials.Launcher.OpenAsync
                |> FrontendHelpers.DoubleCheckCompletionNonGeneric
        ) |> ignore
        mainLayout.Children.Add(transactionHistoryButton)

        let currentConnectivityInstance = Connectivity.NetworkAccess
        if currentConnectivityInstance = NetworkAccess.Internet then
            paymentButton.Text <- paymentCaption
            match accountBalance with
            | Cached(amount,_) ->
                if (amount > 0m) then
                    paymentButton.IsEnabled <- true
            | _ -> ()
        else
            paymentButton.Text <- paymentCaptionInColdStorage
            paymentButton.IsEnabled <- true
            transactionHistoryButton.IsEnabled <- false

        // FIXME: remove this workaround below when https://github.com/xamarin/Xamarin.Forms/issues/8843 gets fixed
        if (Device.RuntimePlatform <> Device.macOS) then () else

        let backButton = Button(Text = "< Go back")
        backButton.Clicked.Subscribe(fun _ ->
            Device.BeginInvokeOnMainThread(fun _ ->
                balancesPage.Navigation.PopAsync() |> FrontendHelpers.DoubleCheckCompletion
            )
        ) |> ignore
        mainLayout.Children.Add(backButton)
        //</workaround>

    member this.OnSendPaymentClicked(sender: Object, args: EventArgs) =
        let newReceivePageFunc = (fun _ ->
            ReceivePage(account, balancesPage, balanceWidgetsFromBalancePage) :> Page
        )
        let sendPage = SendPage(account, this, newReceivePageFunc)

        if paymentButton.Text = paymentCaptionInColdStorage then
            (sendPage :> FrontendHelpers.IAugmentablePayPage).AddTransactionScanner()
        elif paymentButton.Text = paymentCaption then
            ()
        else
            failwith "Initialization of ReceivePage() didn't happen?"

        FrontendHelpers.SwitchToNewPage this sendPage false

    member this.OnCopyToClipboardClicked(sender: Object, args: EventArgs) =
        let copyToClipboardButton = base.FindByName<Button>("copyToClipboardButton")
        FrontendHelpers.ChangeTextAndChangeBack copyToClipboardButton "Copied"

        Clipboard.SetTextAsync account.PublicAddress
            |> FrontendHelpers.DoubleCheckCompletionNonGeneric
