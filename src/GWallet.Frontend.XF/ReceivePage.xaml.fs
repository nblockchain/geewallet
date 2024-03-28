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
                 readOnly: bool,

                 // FIXME: should receive an Async<MaybeCached<decimal>> so that we get a fresh rate, just in case
                 usdRate: MaybeCached<decimal>,

                 balancesPage: Page,
                 balanceWidgetsFromBalancePage: BalanceWidgets) as self =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<ReceivePage>)

    let mainLayout = base.FindByName<Grid>("mainLayout")
    let paymentButton = mainLayout.FindByName<Button> "paymentButton"
    let transactionHistoryButton =
        mainLayout.FindByName<Button> "viewTransactionHistoryButton"

    let paymentCaption = "Send Payment"
    let paymentCaptionInColdStorage = "Signoff Payment Offline"

    let currencyImg =
        mainLayout.FindByName<Image> "currencyImage"

    let balanceLabel = mainLayout.FindByName<Label> "balanceLabel"
    let fiatBalanceLabel = mainLayout.FindByName<Label> "fiatBalanceLabel"

    let TapCryptoAmountLabel accountBalance =
        let cryptoSubUnit =
            if balanceLabel.Text.Contains UtxoCoin.SubUnit.Bits.Caption then
                Some UtxoCoin.SubUnit.Sats
            elif balanceLabel.Text.Contains UtxoCoin.SubUnit.Sats.Caption then
                None
            else
                Some UtxoCoin.SubUnit.Bits
        FrontendHelpers.UpdateBalance
            (NotFresh accountBalance)
            account.Currency
            usdRate
            None
            balanceLabel
            fiatBalanceLabel
            cryptoSubUnit
        |> ignore

    do
        self.Init()

    [<Obsolete(DummyPageConstructorHelper.Warning)>]
    new() = ReceivePage(ReadOnlyAccount(Currency.BTC, { Name = "dummy"; Content = fun _ -> "" }, fun _ -> ""),
                        false,
                        Fresh 0m,
                        DummyPageConstructorHelper.PageFuncToRaiseExceptionIfUsedAtRuntime(),
                        { CryptoLabel = null; FiatLabel = null ; Frame = null })

    member __.Init() =

        let accountBalance =
            Caching.Instance.RetrieveLastCompoundBalance account.PublicAddress account.Currency
        FrontendHelpers.UpdateBalance
            (NotFresh accountBalance)
            account.Currency
            usdRate
            None
            balanceLabel
            fiatBalanceLabel
            None
            |> ignore

        // this below is for the case when a new ReceivePage() instance is suddenly created after sending a transaction
        // (we need to update the balance page ASAP in case the user goes back to it after sending the transaction)
        FrontendHelpers.UpdateBalance (NotFresh accountBalance)
                                      account.Currency
                                      usdRate
                                      (Some balanceWidgetsFromBalancePage.Frame)
                                      balanceWidgetsFromBalancePage.CryptoLabel
                                      balanceWidgetsFromBalancePage.FiatLabel
                                      None
            |> ignore

        balanceLabel.FontSize <- FrontendHelpers.BigFontSize
        fiatBalanceLabel.FontSize <- FrontendHelpers.MediumFontSize

        if account.Currency = Currency.BTC then
            let cryptoTapGestureRecognizer = TapGestureRecognizer()
            cryptoTapGestureRecognizer.Tapped.Subscribe(
                fun _ -> TapCryptoAmountLabel accountBalance
            ) |> ignore
            balanceLabel.GestureRecognizers.Add cryptoTapGestureRecognizer

        let qrCode = mainLayout.FindByName<ZXingBarcodeImageView> "qrCode"
        if isNull qrCode then
            failwith "Couldn't find QR code"
        qrCode.BarcodeValue <- account.PublicAddress
        qrCode.IsVisible <- true

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

        currencyImg.Source <-
            FrontendHelpers.CreateCurrencyImageSource
                account.Currency
                readOnly
                CurrencyImageSize.Big
        currencyImg.IsVisible <- true

        // FIXME: remove this workaround below when https://github.com/xamarin/Xamarin.Forms/issues/8843 gets fixed
        // TODO: file the UWP bug too
        if Device.RuntimePlatform <> Device.macOS && Device.RuntimePlatform <> Device.UWP then () else

        let backButton = Button(Text = "< Go back")
        backButton.Clicked.Subscribe(fun _ ->
            MainThread.BeginInvokeOnMainThread(fun _ ->
                balancesPage.Navigation.PopAsync() |> FrontendHelpers.DoubleCheckCompletion
            )
        ) |> ignore
        mainLayout.Children.Add(backButton)
        //</workaround> (NOTE: this also exists in PairingFromPage.xaml.fs)

    member __.OnViewTransactionHistoryClicked(_sender: Object, _args: EventArgs) =
        BlockExplorer.GetTransactionHistory account
            |> Xamarin.Essentials.Launcher.OpenAsync
            |> FrontendHelpers.DoubleCheckCompletionNonGeneric

    member self.OnSendPaymentClicked(_sender: Object, _args: EventArgs) =
        let newReceivePageFunc = (fun _ ->
            ReceivePage(account, readOnly, usdRate, balancesPage, balanceWidgetsFromBalancePage) :> Page
        )
        let sendPage () =
            let newPage = SendPage(account, self, newReceivePageFunc)

            if paymentButton.Text = paymentCaptionInColdStorage then
                (newPage :> FrontendHelpers.IAugmentablePayPage).AddTransactionScanner()
            elif paymentButton.Text = paymentCaption then
                ()
            else
                failwith "Initialization of ReceivePage() didn't happen?"

            newPage :> Page

        FrontendHelpers.SwitchToNewPage self sendPage false

    member __.OnCopyToClipboardClicked(_sender: Object, _args: EventArgs) =
        let copyToClipboardButton = base.FindByName<Button>("copyToClipboardButton")
        FrontendHelpers.ChangeTextAndChangeBack copyToClipboardButton "Copied"

        Clipboard.SetTextAsync account.PublicAddress
            |> FrontendHelpers.DoubleCheckCompletionNonGeneric
