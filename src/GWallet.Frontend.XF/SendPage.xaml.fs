namespace GWallet.Frontend.XF

open System
open System.Linq
open System.Threading.Tasks

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Backend

type TransactionInfo =
    { Account: NormalAccount;
      Metadata: IBlockchainFeeInfo;
      Destination: string; 
      Amount: TransferAmount; 
      Passphrase: string; }

type SendPage(account: NormalAccount) =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<SendPage>)

    let GetBalance() =
        let baseAccount = account:>IAccount
        // FIXME: should make sure to get the unconfirmed balance
        let cachedBalance = Caching.RetreiveLastBalance (baseAccount.PublicAddress, baseAccount.Currency)
        match cachedBalance with
        | NotAvailable -> failwith "Assertion failed: send page should not be accessed if last balance saved on cache was not > 0"
        | Cached(theCachedBalance,_) -> theCachedBalance

    let lastCachedBalance: decimal = GetBalance()

    member private this.ReenableButtons() =
        let mainLayout = base.FindByName<StackLayout>("mainLayout")
        Device.BeginInvokeOnMainThread(fun _ ->
            let sendButton = mainLayout.FindByName<Button>("sendButton")
            sendButton.IsEnabled <- true
            let cancelButton = mainLayout.FindByName<Button>("cancelButton")
            cancelButton.IsEnabled <- true
            sendButton.Text <- "Send"
        )

    member this.OnAllBalanceButtonClicked(sender: Object, args: EventArgs): unit =
        let mainLayout = base.FindByName<StackLayout>("mainLayout")
        let amountToSend = mainLayout.FindByName<Entry>("amountToSend")
        amountToSend.Text <- (lastCachedBalance.ToString())

    member private this.SendTransaction (transactionInfo: TransactionInfo) =
        let maybeTxId =
            try
                Account.SendPayment transactionInfo.Account
                                    transactionInfo.Metadata
                                    transactionInfo.Destination
                                    transactionInfo.Amount
                                    transactionInfo.Passphrase
                                        |> Async.RunSynchronously |> Some
            with
            | :? DestinationEqualToOrigin ->
                let errMsg = "Transaction's origin cannot be the same as the destination."
                Device.BeginInvokeOnMainThread(fun _ ->
                    this.DisplayAlert("Alert", errMsg, "OK").ContinueWith(fun _ ->
                        this.ReenableButtons()
                    ) |> FrontendHelpers.DoubleCheckCompletion
                )
                None
            | :? InsufficientFunds ->
                let errMsg = "Insufficient funds."
                Device.BeginInvokeOnMainThread(fun _ ->
                    this.DisplayAlert("Alert", errMsg, "OK").ContinueWith(fun _ ->
                        this.ReenableButtons()
                    ) |> FrontendHelpers.DoubleCheckCompletion
                )
                None
            | :? InvalidPassword ->
                let errMsg = "Invalid passphrase, try again."
                Device.BeginInvokeOnMainThread(fun _ ->
                    this.DisplayAlert("Alert", errMsg, "OK").ContinueWith(fun _ ->
                        this.ReenableButtons()
                    ) |> FrontendHelpers.DoubleCheckCompletion
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
                            this.Navigation.PopModalAsync() |> FrontendHelpers.DoubleCheckCompletion
                        )
                    ) |> FrontendHelpers.DoubleCheckCompletion
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
                else if (inputAddress.Length < lengthLimitViolated) then
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

    member this.OnEntryTextChanged(sender: Object, args: EventArgs) =
        let mainLayout = base.FindByName<StackLayout>("mainLayout")
        if (mainLayout = null) then
            //page not yet ready
            ()
        else
            let amountToSend = mainLayout.FindByName<Entry>("amountToSend")
            let passphrase = mainLayout.FindByName<Entry>("passphrase")
            let destinationAddress = mainLayout.FindByName<Entry>("destinationAddress")
            if (destinationAddress = null ||
                passphrase = null ||
                amountToSend = null) then
                ()
            else
                let sendButton = mainLayout.FindByName<Button>("sendButton")
                if (amountToSend.Text <> null && amountToSend.Text.Length > 0) then

                    // FIXME: marking as red should not even mark button as disabled but give the reason in Alert?
                    match Decimal.TryParse(amountToSend.Text) with
                    | false,_ ->
                        amountToSend.TextColor <- Color.Red
                        sendButton.IsEnabled <- false
                    | true,amount ->
                        if (amount <= 0.0m || amount > lastCachedBalance) then
                            amountToSend.TextColor <- Color.Red
                            sendButton.IsEnabled <- false
                        else
                            amountToSend.TextColor <- Color.Default
                            sendButton.IsEnabled <- passphrase.Text <> null && passphrase.Text.Length > 0 &&
                                                    destinationAddress.Text <> null && destinationAddress.Text.Length > 0
                else
                    sendButton.IsEnabled <- false

    member this.OnCancelButtonClicked(sender: Object, args: EventArgs) =
        this.Navigation.PopModalAsync() |> FrontendHelpers.DoubleCheckCompletion

    member private this.DisableButtons() =
        let mainLayout = base.FindByName<StackLayout>("mainLayout")
        let sendButton = mainLayout.FindByName<Button>("sendButton")
        sendButton.IsEnabled <- false
        let cancelButton = mainLayout.FindByName<Button>("cancelButton")
        cancelButton.IsEnabled <- false
        sendButton.Text <- "Sending..."

    member private this.AnswerToFee (txInfo: TransactionInfo) (answer: Task<bool>):unit =
        if (answer.Result) then
            Task.Run(fun _ -> this.SendTransaction txInfo) |> FrontendHelpers.DoubleCheckCompletion
        else
            this.ReenableButtons()

    member private this.ShowFeeAndSend (maybeTxMetadataWithFeeEstimation: Option<IBlockchainFeeInfo>,
                                        transferAmount: TransferAmount,
                                        destinationAddress: string,
                                        passphrase: string) =
        match maybeTxMetadataWithFeeEstimation with
        | None -> ()
        | Some txMetadataWithFeeEstimation ->
            let feeCurrency = txMetadataWithFeeEstimation.Currency
            let usdRateForCurrency = FiatValueEstimation.UsdValue feeCurrency
            match usdRateForCurrency with
            | NotFresh _ ->
                // this probably would never happen, because without internet connection we may get
                // then txFeeInfoTask throw before... so that's why I write the TODO below...
                Device.BeginInvokeOnMainThread(fun _ ->
                    let alertInternetConnTask =
                        this.DisplayAlert("Alert",
                                          // TODO: support cold storage mode here
                                          "Internet connection not available at the moment, try again later",
                                          "OK")
                    alertInternetConnTask.ContinueWith(fun _ -> this.ReenableButtons())
                        |> FrontendHelpers.DoubleCheckCompletion
                )
            | Fresh someUsdValue ->

                let feeInCrypto = txMetadataWithFeeEstimation.FeeValue
                let feeInFiatValue = someUsdValue * feeInCrypto
                let feeInFiatValueStr = sprintf "~ %s USD"
                                                (FrontendHelpers.ShowDecimalForHumans(CurrencyType.Fiat,
                                                                                      feeInFiatValue))

                let feeAskMsg = sprintf "Estimated fee for this transaction would be: %s %s (%s)"
                                      (FrontendHelpers.ShowDecimalForHumans(CurrencyType.Crypto,
                                                                            feeInCrypto))
                                      (txMetadataWithFeeEstimation.Currency.ToString())
                                      feeInFiatValueStr
                Device.BeginInvokeOnMainThread(fun _ ->
                    let askFeeTask = this.DisplayAlert("Alert", feeAskMsg, "OK", "Cancel")

                    let txInfo = { Account = account;
                                   Metadata = txMetadataWithFeeEstimation;
                                   Amount = transferAmount;
                                   Destination = destinationAddress;
                                   Passphrase = passphrase; }

                    askFeeTask.ContinueWith(this.AnswerToFee txInfo) |> FrontendHelpers.DoubleCheckCompletion
                )


    member this.OnSendButtonClicked(sender: Object, args: EventArgs): unit =
        let mainLayout = base.FindByName<StackLayout>("mainLayout")
        let amountToSend = mainLayout.FindByName<Entry>("amountToSend")
        let passphraseEntry = mainLayout.FindByName<Entry>("passphrase")
        let passphrase = passphraseEntry.Text
        let destinationAddressEntry = mainLayout.FindByName<Entry>("destinationAddress")
        let destinationAddress = destinationAddressEntry.Text

        match Decimal.TryParse(amountToSend.Text) with
        | false,_ ->
            this.DisplayAlert("Alert", "The amount should be a decimal amount", "OK")
                |> FrontendHelpers.DoubleCheckCompletion
        | true,amount ->
            if not (amount > 0.0m) then
                this.DisplayAlert("Alert", "Amount should be positive", "OK") |> FrontendHelpers.DoubleCheckCompletion
            else
                let amountRemaining = lastCachedBalance - amount
                if (amountRemaining < 0.0m) then
                    failwith "Somehow the UI didn't avoid the user entering a transfer amount larger than current balance?"
                let transferAmount = TransferAmount(amount, amountRemaining)

                this.DisableButtons()

                let currency = (account:>IAccount).Currency
                let maybeValidatedAddress = this.ValidateAddress currency destinationAddress
                match maybeValidatedAddress with
                | None -> this.ReenableButtons()
                | Some validatedDestinationAddress ->

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
                                alertLowBalanceForFeeTask.ContinueWith(fun _ -> this.ReenableButtons())
                                    |> FrontendHelpers.DoubleCheckCompletion
                            )
                            return None
                    }

                    let maybeTxMetadataWithFeeEstimationTask = maybeTxMetadataWithFeeEstimationAsync |> Async.StartAsTask

                    maybeTxMetadataWithFeeEstimationTask.ContinueWith(fun (txMetadataWithFeeEstimationTask: Task<Option<IBlockchainFeeInfo>>) ->
                        this.ShowFeeAndSend(txMetadataWithFeeEstimationTask.Result,
                                            transferAmount,
                                            validatedDestinationAddress,
                                            passphrase)
                    ) |> FrontendHelpers.DoubleCheckCompletion

                    ()
