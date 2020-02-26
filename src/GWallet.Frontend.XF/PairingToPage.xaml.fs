namespace GWallet.Frontend.XF

open System
open System.Linq

open Xamarin.Forms
open Xamarin.Forms.Xaml
open ZXing.Net.Mobile.Forms

open GWallet.Backend

type PairingToPage(balancesPage: Page,
                   normalAccountsBalanceSets: seq<BalanceSet>,
                   currencyImages: Map<Currency*bool,Image>,
                   newBalancesPageFunc: seq<BalanceState>*seq<BalanceState> -> Page) =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<PairingToPage>)

    let mainLayout = base.FindByName<StackLayout>("mainLayout")
    let scanQrCodeButton = mainLayout.FindByName<Button>("scanQrCode")
    let coldAddressesEntry = mainLayout.FindByName<Entry>("coldStorageAddresses")
    let pairButton = mainLayout.FindByName<Button>("pairButton")
    let cancelButton = mainLayout.FindByName<Button>("cancelButton")
    do
        if Device.RuntimePlatform = Device.Android || Device.RuntimePlatform = Device.iOS then
            scanQrCodeButton.IsVisible <- true

        FrontendHelpers.ApplyMacWorkaroundAgainstInvisibleLabels mainLayout


    let GuessCurrenciesOfAddress (address: string): Async<List<Currency>> = async {
        try
            return! Account.ValidateUnknownCurrencyAddress address
        with
        | AddressMissingProperPrefix _ ->
            return List.empty
        | AddressWithInvalidLength _ ->
            return List.empty
        | AddressWithInvalidChecksum _ ->
            return List.empty
    }

    [<Obsolete(DummyPageConstructorHelper.Warning)>]
    new() = PairingToPage(DummyPageConstructorHelper.PageFuncToRaiseExceptionIfUsedAtRuntime(),
                          Seq.empty,Map.empty,(fun (_,_) -> Page()))

    member this.OnScanQrCodeButtonClicked(sender: Object, args: EventArgs): unit =
        let scanPage = ZXingScannerPage FrontendHelpers.BarCodeScanningOptions
        scanPage.add_OnScanResult(fun result ->
            scanPage.IsScanning <- false

            Device.BeginInvokeOnMainThread(fun _ ->
                // NOTE: modal because otherwise we would see a 2nd topbar added below the 1st topbar when scanning
                //       (saw this behaviour on Android using Xamarin.Forms 3.0.x, re-test/file bug later?)
                let task = this.Navigation.PopModalAsync()
                coldAddressesEntry.Text <- result.Text
                task |> FrontendHelpers.DoubleCheckCompletionNonGeneric
            )
        )
        Device.BeginInvokeOnMainThread(fun _ ->
            // NOTE: modal because otherwise we would see a 2nd topbar added below the 1st topbar when scanning
            //       (saw this behaviour on Android using Xamarin.Forms 3.0.x, re-test/file bug later?)
            this.Navigation.PushModalAsync scanPage
                |> FrontendHelpers.DoubleCheckCompletionNonGeneric
        )

    member this.OnEntryTextChanged(sender: Object, args: EventArgs) =
        Device.BeginInvokeOnMainThread(fun _ ->
            pairButton.IsEnabled <- not (String.IsNullOrEmpty coldAddressesEntry.Text)
        )

    member this.OnCancelButtonClicked(sender: Object, args: EventArgs) =
        Device.BeginInvokeOnMainThread(fun _ ->
            balancesPage.Navigation.PopAsync() |> FrontendHelpers.DoubleCheckCompletion
        )

    member this.OnPairButtonClicked(sender: Object, args: EventArgs): unit =
        let watchWalletInfoJson = coldAddressesEntry.Text
        let watchWalletInfo = Marshalling.Deserialize watchWalletInfoJson

        Device.BeginInvokeOnMainThread(fun _ ->
            pairButton.IsEnabled <- false
            pairButton.Text <- "Pairing..."
            coldAddressesEntry.IsEnabled <- false
            cancelButton.IsEnabled <- false
            scanQrCodeButton.IsEnabled <- false
            coldAddressesEntry.IsEnabled <- false
        )

        async {
            do! Account.CreateReadOnlyAccounts watchWalletInfo

            let readOnlyAccounts = Account.GetAllActiveAccounts().OfType<ReadOnlyAccount>() |> List.ofSeq
                                   |> List.map (fun account -> account :> IAccount)
            let readOnlyAccountsWithWidgets =
                FrontendHelpers.CreateWidgetsForAccounts readOnlyAccounts currencyImages true

            let _,readOnlyAccountsBalancesJob =
                FrontendHelpers.UpdateBalancesAsync readOnlyAccountsWithWidgets false ServerSelectionMode.Fast None

            let _,normalAccountsBalancesJob =
                FrontendHelpers.UpdateBalancesAsync normalAccountsBalanceSets
                                                    true
                                                    ServerSelectionMode.Fast
                                                    None

            let allBalancesJob =
                FSharpUtil.AsyncExtensions.MixedParallel2 normalAccountsBalancesJob readOnlyAccountsBalancesJob
            let! allResolvedNormalAccountBalances,allResolvedReadOnlyBalances = allBalancesJob

            Device.BeginInvokeOnMainThread(fun _ ->
                let newBalancesPage = newBalancesPageFunc(allResolvedNormalAccountBalances,
                                                          allResolvedReadOnlyBalances)
                let navNewBalancesPage = NavigationPage(newBalancesPage)
                NavigationPage.SetHasNavigationBar(newBalancesPage, false)
                NavigationPage.SetHasNavigationBar(navNewBalancesPage, false)

                // FIXME: BalancePage should probably be IDisposable and remove timers when disposing
                balancesPage.Navigation.RemovePage balancesPage

                this.Navigation.InsertPageBefore(navNewBalancesPage, this)

                this.Navigation.PopAsync() |> FrontendHelpers.DoubleCheckCompletion
            )
        } |> FrontendHelpers.DoubleCheckCompletionAsync false


