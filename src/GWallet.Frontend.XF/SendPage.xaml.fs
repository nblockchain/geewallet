namespace GWallet.Frontend.XF

open System
open System.Linq
open System.Threading.Tasks

open Plugin.Connectivity
open Xamarin.Forms
open Xamarin.Forms.Xaml
open ZXing.Net.Mobile.Forms

open GWallet.Backend

type TransactionInfo =
    { Metadata: IBlockchainFeeInfo;
      Destination: string; 
      Amount: TransferAmount;
    }

type TransactionProposal<'T when 'T :> IBlockchainFeeInfo> =
    // hot wallet dealing with normal or readonly account:
    | NotAvailableBecauseOfHotMode
    // cold wallet about to scan proposal from hot wallet:
    | ColdStorageMode of Option<UnsignedTransaction<'T>>
    // hot wallet about to broadcast transaction of ReadOnly account:
    | ColdStorageRemoteControl of Option<SignedTransaction<'T>>

type SendPage(account: IAccount, receivePage: Page, newReceivePageFunc: unit->Page) as this =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<SendPage>)

    let GetCachedBalance() =
        // FIXME: should make sure to get the unconfirmed balance
        Caching.Instance.RetreiveLastCompoundBalance account.PublicAddress account.Currency

    let usdRateAtPageCreation = FiatValueEstimation.UsdValue account.Currency
    let cachedBalanceAtPageCreation = GetCachedBalance()

    let sendCaption = "Send"
    let sendWipCaption = "Sending..."
    let signCaption = "Sign transaction"
    let signWipCaption = "Signing..."
    let broadcastCaption = "Broadcast transaction"
    let broadcastWipCaption = "Broadcasting..."

    let lockObject = Object()
    let mutable transaction = NotAvailableBecauseOfHotMode

    let mainLayout = base.FindByName<StackLayout>("mainLayout")
    let destinationScanQrCodeButton = mainLayout.FindByName<Button> "destinationScanQrCodeButton"
    let transactionScanQrCodeButton = mainLayout.FindByName<Button> "transactionScanQrCodeButton"
    let currencySelectorPicker = mainLayout.FindByName<Picker>("currencySelector")
    let transactionLayout = mainLayout.FindByName<StackLayout> "transactionLayout"
    let transactionLabel = mainLayout.FindByName<Label> "transactionLabel"
    let transactionEntry = mainLayout.FindByName<Entry> "transactionEntry"
    let amountToSend = mainLayout.FindByName<Entry> "amountToSend"
    let destinationAddressEntry = mainLayout.FindByName<Entry> "destinationAddressEntry"
    let allBalanceButton = mainLayout.FindByName<Button> "allBalance"
    let passwordEntry = mainLayout.FindByName<Entry> "passwordEntry"
    let passwordLabel = mainLayout.FindByName<Label> "passwordLabel"
    let sendOrSignButton = mainLayout.FindByName<Button> "sendOrSignButton"
    let cancelButton = mainLayout.FindByName<Button> "cancelButton"
    do
        let accountCurrency = account.Currency.ToString()
        currencySelectorPicker.Items.Add "USD"
        currencySelectorPicker.Items.Add accountCurrency
        currencySelectorPicker.SelectedItem <- accountCurrency
        match usdRateAtPageCreation with
        | NotFresh NotAvailable ->
            currencySelectorPicker.IsEnabled <- false
        | _ -> ()

        if Device.RuntimePlatform = Device.Android || Device.RuntimePlatform = Device.iOS then
            destinationScanQrCodeButton.IsVisible <- true

        sendOrSignButton.Text <- sendCaption
        match account with
        | :? ReadOnlyAccount ->
            Device.BeginInvokeOnMainThread(fun _ ->
                passwordEntry.IsVisible <- false
                passwordLabel.IsVisible <- false
            )
        | _ ->
            if not CrossConnectivity.IsSupported then
                failwith "cross connectivity plugin not supported for this platform?"

            use crossConnectivityInstance = CrossConnectivity.Current
            if not crossConnectivityInstance.IsConnected then
                lock lockObject (fun _ ->
                    transaction <- (ColdStorageMode None)
                )

                (this:>FrontendHelpers.IAugmentablePayPage).AddTransactionScanner()
            this.AdjustWidgetsStateAccordingToConnectivity()

    [<Obsolete(DummyPageConstructorHelper.Warning)>]
    new() = SendPage(ReadOnlyAccount(Currency.BTC, { Name = "dummy"; Content = fun _ -> "" }, fun _ -> ""),
                     DummyPageConstructorHelper.PageFuncToRaiseExceptionIfUsedAtRuntime(),(fun _ -> Page()))

    member private this.AdjustWidgetsStateAccordingToConnectivity() =
        use crossConnectivityInstance = CrossConnectivity.Current
        if not crossConnectivityInstance.IsConnected then
            Device.BeginInvokeOnMainThread(fun _ ->
                currencySelectorPicker.IsEnabled <- false
                amountToSend.IsEnabled <- false
                destinationAddressEntry.IsEnabled <- false
            )

    member this.OnTransactionScanQrCodeButtonClicked(sender: Object, args: EventArgs): unit =
        let mainLayout = base.FindByName<StackLayout> "mainLayout"
        let transactionEntry = mainLayout.FindByName<Entry> "transactionEntry"

        let scanPage = ZXingScannerPage FrontendHelpers.BarCodeScanningOptions
        scanPage.add_OnScanResult(fun (result:ZXing.Result) ->
            // HACK: Sometimes with larger QR codes, the scan is wrong and returns a random number not larger than ~8
            //       characters. As a transaction is much larger (~800-1,200 chars) then we use this hack to not gather
            //       these bullshit results and make the user keep trying.
            if (result.Text.Length > 20) then
                scanPage.IsScanning <- false
                Device.BeginInvokeOnMainThread(fun _ ->
                    let task = this.Navigation.PopModalAsync()
                    task.ContinueWith(fun (t: Task<Page>) ->
                        Device.BeginInvokeOnMainThread(fun _ ->
                            transactionEntry.Text <- result.Text
                        )

                    ) |> FrontendHelpers.DoubleCheckCompletionNonGeneric
                )
        )
        Device.BeginInvokeOnMainThread(fun _ ->
            let alertColdStorageTask =
                this.DisplayAlert("Alert",
                                  FrontendHelpers.BigQrCodeWarning,
                                  "OK")
            alertColdStorageTask.ContinueWith(
                fun _ ->
                    Device.BeginInvokeOnMainThread(fun _ ->
                        this.Navigation.PushModalAsync scanPage
                            |> FrontendHelpers.DoubleCheckCompletionNonGeneric
                    )
            ) |> FrontendHelpers.DoubleCheckCompletionNonGeneric
        )

    member this.OnScanQrCodeButtonClicked(sender: Object, args: EventArgs): unit =
        let mainLayout = base.FindByName<StackLayout>("mainLayout")

        let scanPage = ZXingScannerPage FrontendHelpers.BarCodeScanningOptions
        scanPage.add_OnScanResult(fun result ->
            scanPage.IsScanning <- false

            Device.BeginInvokeOnMainThread(fun _ ->
                let task = this.Navigation.PopModalAsync()
                task.ContinueWith(fun (t: Task<Page>) ->
                    let address,maybeAmount =
                        match account.Currency with
                        | Currency.BTC -> UtxoCoin.Account.ParseAddressOrUrl result.Text
                        | _ -> result.Text,None

                    Device.BeginInvokeOnMainThread(fun _ ->
                        destinationAddressEntry.Text <- address
                    )
                    match maybeAmount with
                    | None -> ()
                    | Some amount ->
                        let amountLabel = mainLayout.FindByName<Entry>("amountToSend")
                        Device.BeginInvokeOnMainThread(fun _ ->
                            let cryptoCurrencyInPicker =
                                currencySelectorPicker.Items.FirstOrDefault(
                                    fun item -> item.ToString() = account.Currency.ToString()
                                )
                            if (cryptoCurrencyInPicker = null) then
                                failwithf "Could not find currency %A in picker?" account.Currency
                            currencySelectorPicker.SelectedItem <- cryptoCurrencyInPicker
                            let aPreviousAmountWasSet = not (String.IsNullOrWhiteSpace amountLabel.Text)
                            amountLabel.Text <- amount.ToString()
                            if aPreviousAmountWasSet then
                                this.DisplayAlert("Alert", "Note: new amount has been set", "OK") |> ignore
                        )
                ) |> FrontendHelpers.DoubleCheckCompletionNonGeneric
            )
        )
        this.Navigation.PushModalAsync scanPage
            |> FrontendHelpers.DoubleCheckCompletionNonGeneric

    member this.OnAllBalanceButtonClicked(sender: Object, args: EventArgs): unit =
        match cachedBalanceAtPageCreation with
        | Cached(cachedBalance,_) ->
            let usdRate = FiatValueEstimation.UsdValue account.Currency
            let mainLayout = base.FindByName<StackLayout>("mainLayout")
            let amountToSend = mainLayout.FindByName<Entry>("amountToSend")

            let allBalanceAmount =
                match currencySelectorPicker.SelectedItem.ToString() with
                | "USD" ->
                    match usdRate with
                    | Fresh rate | NotFresh(Cached(rate,_)) ->
                        cachedBalance * rate
                    | NotFresh NotAvailable ->
                        failwith "if no usdRate was available, currencySelectorPicker should have been disabled, so it shouldn't have 'USD' selected"
                | _ -> cachedBalance
            amountToSend.Text <- allBalanceAmount.ToString()
        | _ ->
            failwith "if no balance was available(offline?), allBalance button should have been disabled"

    member this.OnCurrencySelectorTextChanged(sender: Object, args: EventArgs): unit =

        let currentAmountTypedEntry = mainLayout.FindByName<Entry>("amountToSend")
        let currentAmountTyped = currentAmountTypedEntry.Text
        match Decimal.TryParse currentAmountTyped with
        | false,_ ->
            ()
        | true,decimalAmountTyped ->
            let usdRate = FiatValueEstimation.UsdValue account.Currency
            match usdRate with
            | NotFresh NotAvailable ->
                failwith "if no usdRate was available, currencySelectorPicker should have been disabled, so it shouldn't have 'USD' selected"
            | Fresh rate | NotFresh(Cached(rate,_)) ->

                //FIXME: maybe only use ShowDecimalForHumans if amount in textbox is not allbalance?
                let convertedAmount =
                    // we choose the WithMax overload because we don't want to surpass current allBalance & be red
                    match currencySelectorPicker.SelectedItem.ToString() with
                    | "USD" ->
                        match cachedBalanceAtPageCreation with
                        | Cached(cachedBalance,_) ->
                            FrontendHelpers.ShowDecimalForHumansWithMax CurrencyType.Fiat
                                                                        (rate * decimalAmountTyped)
                                                                        (cachedBalance * rate)
                        | _ ->
                            failwith "if no balance was available(offline?), currencySelectorPicker should have been disabled, so it shouldn't have 'USD' selected"
                    | _ ->
                        Formatting.DecimalAmount CurrencyType.Crypto (decimalAmountTyped / rate)
                currentAmountTypedEntry.Text <- convertedAmount

    member private this.SendTransaction (account: NormalAccount) (transactionInfo: TransactionInfo) (password: string) =
        let maybeTxId =
            try
                Account.SendPayment account
                                    transactionInfo.Metadata
                                    transactionInfo.Destination
                                    transactionInfo.Amount
                                    password
                                        |> Async.RunSynchronously |> Some
            with
            | :? DestinationEqualToOrigin ->
                let errMsg = "Transaction's origin cannot be the same as the destination."
                Device.BeginInvokeOnMainThread(fun _ ->
                    this.DisplayAlert("Alert", errMsg, "OK").ContinueWith(fun _ ->
                        this.ToggleInputWidgetsEnabledOrDisabled true
                    ) |> FrontendHelpers.DoubleCheckCompletionNonGeneric
                )
                None
            | :? InsufficientFunds ->
                let errMsg = "Insufficient funds."
                Device.BeginInvokeOnMainThread(fun _ ->
                    this.DisplayAlert("Alert", errMsg, "OK").ContinueWith(fun _ ->
                        this.ToggleInputWidgetsEnabledOrDisabled true
                    ) |> FrontendHelpers.DoubleCheckCompletionNonGeneric
                )
                None
            | :? InvalidPassword ->
                let errMsg = "Invalid password, try again."
                Device.BeginInvokeOnMainThread(fun _ ->
                    this.DisplayAlert("Alert", errMsg, "OK").ContinueWith(fun _ ->
                        this.ToggleInputWidgetsEnabledOrDisabled true
                    ) |> FrontendHelpers.DoubleCheckCompletionNonGeneric
                )
                None
        
        match maybeTxId with
        | None -> ()
        | Some txIdUrlInBlockExplorer ->
            // TODO: allow linking to tx in a button or something?
            Device.BeginInvokeOnMainThread(fun _ ->
                this.DisplayAlert("Success", "Transaction sent.", "OK")
                    .ContinueWith(fun _ ->
                        Device.BeginInvokeOnMainThread(fun _ ->
                            let newReceivePage = newReceivePageFunc()
                            let navNewReceivePage = NavigationPage(newReceivePage)
                            NavigationPage.SetHasNavigationBar(newReceivePage, false)
                            NavigationPage.SetHasNavigationBar(navNewReceivePage, false)
                            receivePage.Navigation.RemovePage receivePage
                            this.Navigation.InsertPageBefore(navNewReceivePage, this)

                            this.Navigation.PopAsync() |> FrontendHelpers.DoubleCheckCompletion
                        )
                    ) |> FrontendHelpers.DoubleCheckCompletionNonGeneric
            )
    
    member private this.ValidateAddress currency destinationAddress =
        let inputAddress = destinationAddress
        try
            Account.ValidateAddress currency destinationAddress
            Some(destinationAddress)
        with
        | AddressMissingProperPrefix(possiblePrefixes) ->
            let possiblePrefixesStr = String.Join(", ", possiblePrefixes)
            let msg =  (sprintf "Address starts with the wrong prefix. Valid prefixes: %s."
                                    possiblePrefixesStr)
            this.DisplayAlert("Alert", msg, "OK") |> ignore
            None
        | AddressWithInvalidLength(lengthLimitViolated) ->
            let msg =
                if (inputAddress.Length > lengthLimitViolated) then
                    (sprintf "Address should have a length not higher than %d characters, please try again."
                        lengthLimitViolated)
                elif (inputAddress.Length < lengthLimitViolated) then
                    (sprintf "Address should have a length not lower than %d characters, please try again."
                        lengthLimitViolated)
                else
                    failwith (sprintf "Address introduced '%s' gave a length error with a limit that matches its length: %d=%d"
                                 inputAddress lengthLimitViolated inputAddress.Length)
            this.DisplayAlert("Alert", msg, "OK") |> ignore
            None
        | AddressWithInvalidChecksum(maybeAddressWithValidChecksum) ->
            let final =
                match maybeAddressWithValidChecksum with
                | None -> None
                | _ ->
                    //FIXME: warn user about bad checksum in any case (not only if the original address has mixed
                    // lowecase and uppercase like if had been validated, to see if he wants to continue or not
                    // (this text is better borrowed from the Frontend.Console project)
                    if not (destinationAddress.All(fun char -> Char.IsLower char)) then
                        None
                    else
                        maybeAddressWithValidChecksum
            if final.IsNone then
                let msg = "Address doesn't seem to be valid, please try again."
                this.DisplayAlert("Alert", msg, "OK") |> ignore
            final

    member private this.IsPasswordUnfilledAndNeeded (mainLayout: StackLayout) =
        match account with
        | :? ReadOnlyAccount ->
            false
        | _ ->
            if passwordEntry = null then
                // not ready yet?
                true
            else
                String.IsNullOrEmpty passwordEntry.Text

    member this.OnTransactionEntryTextChanged (sender: Object, args: EventArgs): unit =
        let mainLayout = base.FindByName<StackLayout> "mainLayout"
        let transactionEntry = mainLayout.FindByName<Entry> "transactionEntry"
        let transactionEntryText = transactionEntry.Text
        if not (String.IsNullOrWhiteSpace transactionEntryText) then
            let maybeTransaction =
                try
                    Account.ImportTransactionFromJson transactionEntryText |> Some
                with
                | :? DeserializationException as dex ->
                    Device.BeginInvokeOnMainThread(fun _ ->
                        transactionEntry.TextColor <- Color.Red
                        let errMsg = "Transaction corrupt or invalid"
                        this.DisplayAlert("Alert", errMsg, "OK") |> FrontendHelpers.DoubleCheckCompletionNonGeneric
                    )
                    None

            match maybeTransaction with
            | None -> ()
            | Some (Unsigned unsignedTransaction) ->
                if account.Currency <> unsignedTransaction.Proposal.Amount.Currency then
                    Device.BeginInvokeOnMainThread(fun _ ->
                        transactionEntry.TextColor <- Color.Red
                        let err =
                            sprintf "Transaction proposal's currency (%A) doesn't match with this currency's account (%A)"
                                    unsignedTransaction.Proposal.Amount.Currency account.Currency
                        this.DisplayAlert("Alert", err, "OK") |> FrontendHelpers.DoubleCheckCompletionNonGeneric
                    )
                elif account.PublicAddress <> unsignedTransaction.Proposal.OriginAddress then
                    Device.BeginInvokeOnMainThread(fun _ ->
                        transactionEntry.TextColor <- Color.Red
                        let err = "Transaction proposal's sender address doesn't match with this currency's account"
                        this.DisplayAlert("Alert", err, "OK") |> FrontendHelpers.DoubleCheckCompletionNonGeneric
                    )
                else
                    // to locally save balances and fiat rates from the online device
                    Caching.Instance.SaveSnapshot unsignedTransaction.Cache

                    lock lockObject (fun _ ->
                        transaction <- (ColdStorageMode (Some unsignedTransaction))
                    )

                    Device.BeginInvokeOnMainThread(fun _ ->
                        transactionEntry.TextColor <- Color.Default
                        destinationAddressEntry.Text <- unsignedTransaction.Proposal.DestinationAddress
                        amountToSend.Text <- unsignedTransaction.Proposal.Amount.ValueToSend.ToString()
                        passwordEntry.Focus() |> ignore
                    )
            | Some (Signed signedTransaction) ->
                if account.Currency <> signedTransaction.TransactionInfo.Proposal.Amount.Currency then
                    Device.BeginInvokeOnMainThread(fun _ ->
                        transactionEntry.TextColor <- Color.Red
                        let err =
                            sprintf "Transaction's currency (%A) doesn't match with this currency's account (%A)"
                                    signedTransaction.TransactionInfo.Proposal.Amount.Currency account.Currency
                        this.DisplayAlert("Alert", err, "OK") |> FrontendHelpers.DoubleCheckCompletionNonGeneric
                    )
                elif account.PublicAddress <> signedTransaction.TransactionInfo.Proposal.OriginAddress then
                    Device.BeginInvokeOnMainThread(fun _ ->
                        transactionEntry.TextColor <- Color.Red
                        let err = "Transaction's sender address doesn't match with this currency's account"
                        this.DisplayAlert("Alert", err, "OK") |> FrontendHelpers.DoubleCheckCompletionNonGeneric
                    )
                else
                    lock lockObject (fun _ ->
                        transaction <- (ColdStorageRemoteControl (Some signedTransaction))
                    )
                    Device.BeginInvokeOnMainThread(fun _ ->
                        transactionEntry.TextColor <- Color.Default
                        sendOrSignButton.IsEnabled <- true
                    )
        ()

    member private this.UpdateEquivalentFiatLabel (): bool =
        let amountToSend = mainLayout.FindByName<Entry>("amountToSend")
        if amountToSend = null || String.IsNullOrWhiteSpace amountToSend.Text then
            false
        else
            let equivalentAmount = mainLayout.FindByName<Label> "equivalentAmountInAlternativeCurrency"
            // FIXME: marking as red should not even mark button as disabled but give the reason in Alert?
            match Decimal.TryParse(amountToSend.Text) with
            | false,_ ->
                amountToSend.TextColor <- Color.Red
                sendOrSignButton.IsEnabled <- false
                equivalentAmount.Text <- String.Empty
                false
            | true,amount ->
                let usdRate = FiatValueEstimation.UsdValue account.Currency
                let lastCachedBalance: decimal =
                    match GetCachedBalance() with
                    | Cached(lastCachedBalance,_) ->
                        lastCachedBalance
                    | _ ->
                        failwith "there should be a cached balance (either by being online, or because of importing a cache snapshot) at the point of changing the amount or destination address (respectively, by the user, or by importing a tx proposal)"

                let allBalanceInSelectedCurrency =
                    match currencySelectorPicker.SelectedItem.ToString() with
                    | "USD" ->
                        match usdRate with
                        | NotFresh NotAvailable ->
                            failwith "if no usdRate was available, currencySelectorPicker should have been disabled, so it shouldn't have 'USD' selected"
                        | NotFresh(Cached(rate,_)) | Fresh rate ->
                            lastCachedBalance * rate
                    | _ -> lastCachedBalance

                if (amount <= 0.0m || amount > allBalanceInSelectedCurrency) then
                    amountToSend.TextColor <- Color.Red
                    if (amount > 0.0m) then
                        equivalentAmount.Text <- "(Not enough funds)"
                    false
                else
                    amountToSend.TextColor <- Color.Default

                    match usdRate with
                    | NotFresh NotAvailable ->
                        true
                    | NotFresh(Cached(rate,_)) | Fresh rate ->
                        let eqAmount,otherCurrency =
                            match currencySelectorPicker.SelectedItem.ToString() with
                            | "USD" ->
                                Formatting.DecimalAmount CurrencyType.Crypto (amount / rate),
                                    account.Currency.ToString()
                            | _ ->
                                Formatting.DecimalAmount CurrencyType.Fiat (rate * amount),
                                    "USD"
                        let usdAmount = sprintf "~ %s %s" eqAmount otherCurrency
                        equivalentAmount.Text <- usdAmount
                        true

    member this.OnEntryTextChanged(sender: Object, args: EventArgs) =
        let mainLayout = base.FindByName<StackLayout>("mainLayout")
        if (mainLayout = null) then
            //page not yet ready
            ()
        else
            let sendOrSignButtonEnabled = this.UpdateEquivalentFiatLabel() &&
                                          destinationAddressEntry <> null &&
                                          (not (String.IsNullOrEmpty destinationAddressEntry.Text)) &&
                                          (not (this.IsPasswordUnfilledAndNeeded mainLayout))
            Device.BeginInvokeOnMainThread(fun _ ->
                sendOrSignButton.IsEnabled <- sendOrSignButtonEnabled
            )

    member this.OnCancelButtonClicked(sender: Object, args: EventArgs) =
        Device.BeginInvokeOnMainThread(fun _ ->
            receivePage.Navigation.PopAsync() |> FrontendHelpers.DoubleCheckCompletion
        )

    member private this.ToggleInputWidgetsEnabledOrDisabled (enabled: bool) =
        let transactionScanQrCodeButton = mainLayout.FindByName<Button> "transactionScanQrCodeButton"
        let destinationScanQrCodeButton = mainLayout.FindByName<Button> "destinationScanQrCodeButton"
        let destinationAddressEntry = mainLayout.FindByName<Entry> "destinationAddressEntry"
        let allBalanceButton = mainLayout.FindByName<Button> "allBalance"
        let currencySelectorPicker = mainLayout.FindByName<Picker> "currencySelector"
        let amountToSendEntry = mainLayout.FindByName<Entry> "amountToSend"
        let passwordEntry = mainLayout.FindByName<Entry> "passwordEntry"

        let newSendOrSignButtonCaption =
            if sendOrSignButton.Text = sendCaption || sendOrSignButton.Text = sendWipCaption then
                if enabled then
                    sendCaption
                else
                    sendWipCaption
            elif sendOrSignButton.Text = broadcastCaption || sendOrSignButton.Text = broadcastWipCaption then
                if enabled then
                    broadcastCaption
                else
                    broadcastWipCaption
            else
                if enabled then
                    signCaption
                else
                    signWipCaption

        Device.BeginInvokeOnMainThread(fun _ ->
            sendOrSignButton.IsEnabled <- enabled
            cancelButton.IsEnabled <- enabled
            transactionScanQrCodeButton.IsEnabled <- enabled
            destinationScanQrCodeButton.IsEnabled <- enabled
            destinationAddressEntry.IsEnabled <- enabled
            allBalanceButton.IsEnabled <- enabled
            currencySelectorPicker.IsEnabled <- enabled
            amountToSendEntry.IsEnabled <- enabled
            passwordEntry.IsEnabled <- enabled
            sendOrSignButton.Text <- newSendOrSignButtonCaption
        )

        this.AdjustWidgetsStateAccordingToConnectivity()

    member private this.SignTransaction (normalAccount: NormalAccount) (unsignedTransaction) (password): unit =
        let maybeRawTransaction =
            try
                Account.SignTransaction normalAccount
                                        unsignedTransaction.Proposal.DestinationAddress
                                        unsignedTransaction.Proposal.Amount
                                        unsignedTransaction.Metadata
                                        password |> Some
            with
            | :? InvalidPassword ->
                let errMsg = "Invalid password, try again."
                Device.BeginInvokeOnMainThread(fun _ ->
                    this.DisplayAlert("Alert", errMsg, "OK").ContinueWith(fun _ ->
                        this.ToggleInputWidgetsEnabledOrDisabled true
                    ) |> FrontendHelpers.DoubleCheckCompletionNonGeneric
                )
                None
        match maybeRawTransaction with
        | None -> ()
        | Some rawTransaction ->
            let signedTransaction = { TransactionInfo = unsignedTransaction; RawTransaction = rawTransaction }
            let compressedTransaction = Account.SerializeSignedTransaction signedTransaction true
            let pairSignedTransactionPage =
                PairingFromPage(this, "Copy signed transaction to the clipboard", compressedTransaction, None)
            NavigationPage.SetHasNavigationBar(pairSignedTransactionPage, false)
            let navPairPage = NavigationPage pairSignedTransactionPage
            NavigationPage.SetHasNavigationBar(navPairPage, false)
            Device.BeginInvokeOnMainThread(fun _ ->
                this.Navigation.PushAsync navPairPage
                    |> FrontendHelpers.DoubleCheckCompletionNonGeneric
            )

    member private this.AnswerToFee (account: IAccount) (txInfo: TransactionInfo) (answer: Task<bool>):unit =
        if (answer.Result) then
            match account with
            | :? NormalAccount as normalAccount ->
                let passwordEntry = mainLayout.FindByName<Entry> "passwordEntry"
                let password = passwordEntry.Text
                match lock lockObject (fun _ -> transaction) with
                | NotAvailableBecauseOfHotMode ->
                    Task.Run(fun _ -> this.SendTransaction normalAccount txInfo password)
                        |> FrontendHelpers.DoubleCheckCompletionNonGeneric
                | ColdStorageMode (None) ->
                    failwith "Fee dialog should not have been shown if no transaction proposal was stored"
                | ColdStorageMode (Some someTransactionProposal) ->
                    this.SignTransaction normalAccount someTransactionProposal password
                | ColdStorageRemoteControl _ ->
                    failwith "remote control should only happen in ReadOnly account handling"

            | :? ReadOnlyAccount as readOnlyAccount ->
                let proposal = {
                    OriginAddress = account.PublicAddress;
                    Amount = txInfo.Amount;
                    DestinationAddress = txInfo.Destination;
                }
                let compressedTxProposal = Account.SerializeUnsignedTransaction proposal txInfo.Metadata true

                let shortColdMsg =
                    "Account belongs to cold storage, so you'll need to scan this as a transaction proposal in the next page."
                let coldMsg =
                    shortColdMsg + " " + FrontendHelpers.BigQrCodeWarning
                Device.BeginInvokeOnMainThread(fun _ ->
                    let alertColdStorageTask =
                        this.DisplayAlert("Alert",
                                          coldMsg,
                                          "OK")
                    alertColdStorageTask.ContinueWith(
                        fun _ ->
                            let pairTransactionProposalPage =
                                PairingFromPage(this,
                                                "Copy proposal to the clipboard",
                                                compressedTxProposal,
                                                Some ("Next step", this:>FrontendHelpers.IAugmentablePayPage))
                            NavigationPage.SetHasNavigationBar(pairTransactionProposalPage, false)
                            let navPairPage = NavigationPage pairTransactionProposalPage
                            NavigationPage.SetHasNavigationBar(navPairPage, false)
                            Device.BeginInvokeOnMainThread(fun _ ->
                                this.Navigation.PushAsync navPairPage
                                    |> FrontendHelpers.DoubleCheckCompletionNonGeneric
                            )

                    ) |> FrontendHelpers.DoubleCheckCompletionNonGeneric
                )

            | _ ->
                failwith "Unexpected SendPage instance running on weird account type"
        else
            this.ToggleInputWidgetsEnabledOrDisabled true

    member private this.ShowFeeAndSend (maybeTxMetadataWithFeeEstimation: Option<IBlockchainFeeInfo>,
                                        transferAmount: TransferAmount,
                                        destinationAddress: string) =
        match maybeTxMetadataWithFeeEstimation with
        // FIXME: should maybe do () in the caller of ShowFeeAndSend() -> change param from Option<IBFI> to just IBFI
        | None -> ()
        | Some txMetadataWithFeeEstimation ->
            let feeCurrency = txMetadataWithFeeEstimation.Currency
            let usdRateForCurrency = FiatValueEstimation.UsdValue feeCurrency
            match usdRateForCurrency with
            | NotFresh NotAvailable ->
                Device.BeginInvokeOnMainThread(fun _ ->
                    let alertInternetConnTask =
                        this.DisplayAlert("Alert",
                                          "Internet connection not available at the moment, try again later",
                                          "OK")
                    alertInternetConnTask.ContinueWith(fun _ -> this.ToggleInputWidgetsEnabledOrDisabled true)
                        |> FrontendHelpers.DoubleCheckCompletionNonGeneric
                )
            | Fresh someUsdValue | NotFresh (Cached(someUsdValue,_)) ->

                let feeInCrypto = txMetadataWithFeeEstimation.FeeValue
                let feeInFiatValue = someUsdValue * feeInCrypto
                let feeInFiatValueStr = sprintf "~ %s USD"
                                                (Formatting.DecimalAmount CurrencyType.Fiat feeInFiatValue)

                let feeAskMsg = sprintf "Estimated fee for this transaction would be: %s %s (%s)"
                                      (Formatting.DecimalAmount CurrencyType.Crypto feeInCrypto)
                                      (txMetadataWithFeeEstimation.Currency.ToString())
                                      feeInFiatValueStr
                Device.BeginInvokeOnMainThread(fun _ ->
                    let askFeeTask = this.DisplayAlert("Alert", feeAskMsg, "OK", "Cancel")

                    let txInfo = { Metadata = txMetadataWithFeeEstimation;
                                   Amount = transferAmount;
                                   Destination = destinationAddress; }

                    askFeeTask.ContinueWith(this.AnswerToFee account txInfo) |> FrontendHelpers.DoubleCheckCompletion
                )


    member this.OnSendOrSignButtonClicked(sender: Object, args: EventArgs): unit =
        let mainLayout = base.FindByName<StackLayout>("mainLayout")
        let amountToSend = mainLayout.FindByName<Entry>("amountToSend")
        let destinationAddress = destinationAddressEntry.Text

        match Decimal.TryParse(amountToSend.Text) with
        | false,_ ->
            this.DisplayAlert("Alert", "The amount should be a decimal amount", "OK")
                |> FrontendHelpers.DoubleCheckCompletionNonGeneric
        | true,amount ->
            if not (amount > 0.0m) then
                this.DisplayAlert("Alert", "Amount should be positive", "OK")
                    |> FrontendHelpers.DoubleCheckCompletionNonGeneric
            else

                let amountInAccountCurrency =
                    match currencySelectorPicker.SelectedItem.ToString() with
                    | "USD" ->

                        // FIXME: we should probably just grab the amount from the equivalentAmountInAlternativeCurrency
                        // label to prevent rate difference from the moment the amount was written until the "Send"
                        // button was pressed
                        let usdRate = FiatValueEstimation.UsdValue account.Currency

                        match usdRate with
                        | Fresh rate | NotFresh(Cached(rate,_)) ->
                            amount / rate
                        | NotFresh NotAvailable ->
                            failwith "if no usdRate was available, currencySelectorPicker should have been disabled, so it shouldn't have 'USD' selected"
                    | _ -> amount

                let currency = account.Currency

                let transferAmount =
                    match GetCachedBalance() with
                    | Cached(lastCachedBalance,_) ->
                        TransferAmount(amountInAccountCurrency, lastCachedBalance, currency)
                    | _ ->
                        failwith "there should be a cached balance (either by being online, or because of importing a cache snapshot) at the point of clicking the send button"

                this.ToggleInputWidgetsEnabledOrDisabled false

                let maybeValidatedAddress = this.ValidateAddress currency destinationAddress
                match maybeValidatedAddress with
                | None -> this.ToggleInputWidgetsEnabledOrDisabled true
                | Some validatedDestinationAddress ->

                    match lock lockObject (fun _ -> transaction) with
                    | ColdStorageMode (None) ->
                        failwith "Sign button should not have been enabled if no transaction proposal was stored"
                    | ColdStorageMode (Some someTransactionProposal) ->
                        // FIXME: convert TransferAmount type into a record, to make it have default Equals() behaviour
                        //        so that we can simplify the below 3 conditions of the `if` statements into 1:
                        if (transferAmount.ValueToSend <> someTransactionProposal.Proposal.Amount.ValueToSend) ||
                           (transferAmount.Currency <> someTransactionProposal.Proposal.Amount.Currency) ||
                           (transferAmount.BalanceAtTheMomentOfSending <>
                               someTransactionProposal.Proposal.Amount.BalanceAtTheMomentOfSending) then
                            failwith "Amount's entry should have been disabled (readonly), but somehow it wasn't because it ended up being different than the transaction proposal"
                        if (validatedDestinationAddress <> someTransactionProposal.Proposal.DestinationAddress) then
                            failwith "Destination's entry should have been disabled (readonly), but somehow it wasn't because it ended up being different than the transaction proposal"

                        this.ShowFeeAndSend(Some someTransactionProposal.Metadata,
                                            someTransactionProposal.Proposal.Amount,
                                            validatedDestinationAddress)

                    | ColdStorageRemoteControl maybeSignedTransaction ->
                        match maybeSignedTransaction with
                        | None -> failwith "if broadcast button was enabled, signed transaction should have been deserialized already"
                        | Some signedTransaction ->
                            let broadcastTask = Async.StartAsTask (Account.BroadcastTransaction signedTransaction)
                            broadcastTask.ContinueWith(fun (t: Task<Uri>) ->
                                Device.BeginInvokeOnMainThread(fun _ ->
                                    this.DisplayAlert("Success", "Transaction sent.", "OK")
                                        .ContinueWith(fun _ ->
                                            Device.BeginInvokeOnMainThread(fun _ ->
                                                let newReceivePage = newReceivePageFunc()
                                                let navNewReceivePage = NavigationPage(newReceivePage)
                                                NavigationPage.SetHasNavigationBar(newReceivePage, false)
                                                NavigationPage.SetHasNavigationBar(navNewReceivePage, false)
                                                receivePage.Navigation.RemovePage receivePage
                                                this.Navigation.InsertPageBefore(navNewReceivePage, this)

                                                this.Navigation.PopAsync() |> FrontendHelpers.DoubleCheckCompletion
                                            )
                                        ) |> FrontendHelpers.DoubleCheckCompletionNonGeneric
                                )
                            ) |> FrontendHelpers.DoubleCheckCompletionNonGeneric

                    | NotAvailableBecauseOfHotMode ->
                        let maybeTxMetadataWithFeeEstimationAsync = async {
                            try
                                let! txMetadataWithFeeEstimation =
                                    Account.EstimateFee account transferAmount validatedDestinationAddress
                                return Some txMetadataWithFeeEstimation
                            with
                            | :? InsufficientBalanceForFee ->
                                Device.BeginInvokeOnMainThread(fun _ ->
                                    let alertLowBalanceForFeeTask =
                                        this.DisplayAlert("Alert",
                                                          // TODO: support cold storage mode here
                                                          "Remaining balance would be too low for the estimated fee, try sending lower amount",
                                                          "OK")
                                    alertLowBalanceForFeeTask.ContinueWith(
                                        fun _ -> this.ToggleInputWidgetsEnabledOrDisabled true
                                    )
                                        |> FrontendHelpers.DoubleCheckCompletionNonGeneric
                                )
                                return None
                        }

                        let maybeTxMetadataWithFeeEstimationTask = maybeTxMetadataWithFeeEstimationAsync |> Async.StartAsTask

                        maybeTxMetadataWithFeeEstimationTask.ContinueWith(fun (txMetadataWithFeeEstimationTask: Task<Option<IBlockchainFeeInfo>>) ->
                            this.ShowFeeAndSend(txMetadataWithFeeEstimationTask.Result,
                                                transferAmount,
                                                validatedDestinationAddress)
                        ) |> FrontendHelpers.DoubleCheckCompletionNonGeneric

                    ()

    interface FrontendHelpers.IAugmentablePayPage with
        member this.AddTransactionScanner() =
            Device.BeginInvokeOnMainThread(fun _ ->
                transactionLayout.IsVisible <- true
                transactionEntry.Text <- String.Empty
                transactionEntry.IsVisible <- true
                transactionScanQrCodeButton.IsEnabled <- true
                if Device.RuntimePlatform = Device.Android || Device.RuntimePlatform = Device.iOS then
                    transactionScanQrCodeButton.IsVisible <- true
                destinationScanQrCodeButton.IsVisible <- false
                allBalanceButton.IsVisible <- false

                if sendOrSignButton.Text = sendWipCaption then
                    transactionLabel.Text <- "Signed transaction:"
                    sendOrSignButton.Text <- broadcastCaption
                    cancelButton.IsEnabled <- true
                else
                    sendOrSignButton.Text <- signCaption
            )
