namespace GWallet.Frontend.XF

open System
open System.Linq

open Xamarin.Forms
open Xamarin.Forms.Xaml
open Xamarin.Essentials
open ZXing.Net.Mobile.Forms
open Fsdk

open GWallet.Backend

type PairingToPage(balancesPage: Page,
                   normalAccountsBalanceSets: seq<BalanceSet>,
                   currencyImages: Map<Currency*bool,Image>,
                   newBalancesPageFunc: seq<BalanceState>*seq<BalanceState> -> Page) =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<PairingToPage>)

    let mainLayout = base.FindByName<Grid> "mainLayout"
    let scanQrCodeButton = mainLayout.FindByName<Button>("scanQrCode")
    let coldAddressesEntry = mainLayout.FindByName<Entry>("coldStorageAddresses")
    let pairButton = mainLayout.FindByName<Button>("pairButton")
    let cancelButton = mainLayout.FindByName<Button>("cancelButton")

    let Deserialize watchWalletInfoJson =
        try
            Marshalling.Deserialize watchWalletInfoJson
            |> Some
        with
        | :? InvalidJson ->
            None
    do
        if FrontendHelpers.PlatformIsCapableOfBarCodeScanning then
            scanQrCodeButton.IsVisible <- true

    [<Obsolete(DummyPageConstructorHelper.Warning)>]
    new() = PairingToPage(DummyPageConstructorHelper.PageFuncToRaiseExceptionIfUsedAtRuntime(),
                          Seq.empty,Map.empty,(fun (_,_) -> Page()))

    member self.OnScanQrCodeButtonClicked(_sender: Object, _args: EventArgs): unit =
        let scanPage = ZXingScannerPage FrontendHelpers.BarCodeScanningOptions
        scanPage.add_OnScanResult(fun result ->
            scanPage.IsScanning <- false

            MainThread.BeginInvokeOnMainThread(fun _ ->
                // NOTE: modal because otherwise we would see a 2nd topbar added below the 1st topbar when scanning
                //       (saw this behaviour on Android using Xamarin.Forms 3.0.x, re-test/file bug later?)
                let task = self.Navigation.PopModalAsync()
                coldAddressesEntry.Text <- result.Text
                task |> FrontendHelpers.DoubleCheckCompletionNonGeneric
            )
        )
        MainThread.BeginInvokeOnMainThread(fun _ ->
            // NOTE: modal because otherwise we would see a 2nd topbar added below the 1st topbar when scanning
            //       (saw this behaviour on Android using Xamarin.Forms 3.0.x, re-test/file bug later?)
            self.Navigation.PushModalAsync scanPage
                |> FrontendHelpers.DoubleCheckCompletionNonGeneric
        )

    member __.OnEntryTextChanged(_sender: Object, _args: EventArgs) =
        MainThread.BeginInvokeOnMainThread(fun _ ->
            pairButton.IsEnabled <- not (String.IsNullOrEmpty coldAddressesEntry.Text)
        )

    member __.OnCancelButtonClicked(_sender: Object, _args: EventArgs) =
        MainThread.BeginInvokeOnMainThread(fun _ ->
            balancesPage.Navigation.PopAsync() |> FrontendHelpers.DoubleCheckCompletion
        )

    member self.OnPairButtonClicked(_sender: Object, _args: EventArgs): unit =
        let watchWalletInfoJson = coldAddressesEntry.Text
        match Deserialize watchWalletInfoJson with
        | None ->
            let msg = "Invalid pairing info format (should be JSON). Did you pair a QR-code from another geewallet instance?"
            MainThread.BeginInvokeOnMainThread(fun _ ->
                self.DisplayAlert("Alert", msg, "OK")
                    |> FrontendHelpers.DoubleCheckCompletionNonGeneric
            )
        | Some watchWalletInfo ->

            MainThread.BeginInvokeOnMainThread(fun _ ->
                pairButton.IsEnabled <- false
                pairButton.Text <- "Pairing..."
                coldAddressesEntry.IsEnabled <- false
                cancelButton.IsEnabled <- false
                scanQrCodeButton.IsEnabled <- false
                coldAddressesEntry.IsEnabled <- false
            )

            async {
                do! Account.CreateReadOnlyAccounts watchWalletInfo

                let readOnlyAccounts =
                    Account.GetAllActiveAccounts().OfType<ReadOnlyAccount>()
                    |> List.ofSeq
                    |> List.map (fun account -> account :> IAccount)
                let readOnlyAccountsWithWidgets =
                    FrontendHelpers.CreateWidgetsForAccounts
                        readOnlyAccounts currencyImages true

                let _,readOnlyAccountsBalancesJob =
                    FrontendHelpers.UpdateBalancesAsync
                        readOnlyAccountsWithWidgets
                        false
                        ServerSelectionMode.Fast
                        None

                let _,normalAccountsBalancesJob =
                    FrontendHelpers.UpdateBalancesAsync
                        normalAccountsBalanceSets
                        true
                        ServerSelectionMode.Fast
                        None

                let allBalancesJob =
                    FSharpUtil.AsyncExtensions.MixedParallel2
                        normalAccountsBalancesJob
                        readOnlyAccountsBalancesJob
                let! allResolvedNormalAccountBalances,allResolvedReadOnlyBalances =
                    allBalancesJob

                MainThread.BeginInvokeOnMainThread(fun _ ->
                    let newBalancesPage =
                        newBalancesPageFunc(
                            allResolvedNormalAccountBalances,
                            allResolvedReadOnlyBalances
                        )
                    let navNewBalancesPage = NavigationPage(newBalancesPage)
                    NavigationPage.SetHasNavigationBar(newBalancesPage, false)
                    NavigationPage.SetHasNavigationBar(navNewBalancesPage, false)

                    // FIXME: BalancePage should probably be IDisposable and remove timers when disposing
                    balancesPage.Navigation.RemovePage balancesPage

                    self.Navigation.InsertPageBefore(navNewBalancesPage, self)

                    self.Navigation.PopAsync()
                    |> FrontendHelpers.DoubleCheckCompletion
                )
            } |> FrontendHelpers.DoubleCheckCompletionAsync false


