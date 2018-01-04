namespace GWallet.Frontend.XF

open System
open System.Threading.Tasks

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Backend

type CurrencyType =
    Fiat | Crypto

type TransactionInfo =
    { Account: NormalAccount;
      Metadata: IBlockchainFeeInfo;
      Destination: string; 
      Amount: TransferAmount; 
      Passphrase: string; }

type SendPage(account: NormalAccount) =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<SendPage>)

    //FIXME: borrowed this function from Frontend.Console, reuse
    let ShowDecimalForHumans currencyType (amount: decimal): string =
        let amountOfDecimalsToShow =
            match currencyType with
            | CurrencyType.Fiat -> 2
            | CurrencyType.Crypto -> 5

        Math.Round(amount, amountOfDecimalsToShow)

            // line below is to add thousand separators and not show zeroes on the right...
            .ToString("N" + amountOfDecimalsToShow.ToString())

    member private this.SendTransaction (transactionInfo: TransactionInfo) =
        let maybeTxId =
            try
                Account.SendPayment transactionInfo.Account
                                    transactionInfo.Metadata
                                    transactionInfo.Destination
                                    transactionInfo.Amount
                                    transactionInfo.Passphrase
                                        |> Some
            with
            | :? DestinationEqualToOrigin ->
                let errMsg = "Transaction's origin cannot be the same as the destination."
                Device.BeginInvokeOnMainThread(fun _ ->
                    this.DisplayAlert("Alert", errMsg, "OK") |> ignore
                )
                None
            | :? InsufficientFunds ->
                let errMsg = "Insufficient funds."
                Device.BeginInvokeOnMainThread(fun _ ->
                    this.DisplayAlert("Alert", errMsg, "OK") |> ignore
                )
                None
            | :? InvalidPassword ->
                let errMsg = "Invalid passphrase, try again."
                Device.BeginInvokeOnMainThread(fun _ ->
                    this.DisplayAlert("Alert", errMsg, "OK") |> ignore
                )
                None
        
        match maybeTxId with
        | None -> ()
        | Some(txId) ->
            Device.BeginInvokeOnMainThread(fun _ ->
                this.DisplayAlert("Alert", "Transaction sent: " + txId, "OK")
                    .ContinueWith(fun _ ->
                        Device.BeginInvokeOnMainThread(fun _ ->
                            this.Navigation.PopModalAsync() |> ignore
                        ) |> ignore
                    ) |> ignore
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
        | AddressWithInvalidChecksum(addressWithValidChecksum) ->
            //FIXME: warn user about bad checksum, to see if he wants to continue or not
            // (this text is better borrowed from the Frontend.Console project)
            Some(addressWithValidChecksum)
                
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
            elif (destinationAddress.Text <> null && destinationAddress.Text.Length > 0 &&
                  amountToSend.Text <> null && amountToSend.Text.Length > 0 &&
                  passphrase.Text <> null && passphrase.Text.Length > 0) then
                  let sendButton = mainLayout.FindByName<Button>("sendButton")
                  sendButton.IsEnabled <- true

    member this.OnCancelButtonClicked(sender: Object, args: EventArgs) =
        this.Navigation.PopModalAsync() |> ignore

    member private this.AnswerToFee (txInfo: TransactionInfo) (answer: Task<bool>):unit =
        if (answer.Result) then
            this.SendTransaction txInfo

    member this.OnSendButtonClicked(sender: Object, args: EventArgs) =
        let mainLayout = base.FindByName<StackLayout>("mainLayout")
        let amountToSend = mainLayout.FindByName<Entry>("amountToSend")
        let passphrase = mainLayout.FindByName<Entry>("passphrase")
        let destinationAddress = mainLayout.FindByName<Entry>("destinationAddress")

        match Decimal.TryParse(amountToSend.Text) with
        | false,_ -> this.DisplayAlert("Alert", "The amount should be a decimal amount", "OK") |> ignore
        | true,amount ->
            if not (amount > 0.0m) then
                this.DisplayAlert("Alert", "Amount should be positive", "OK") |> ignore
            else
                let currency = (account:>IAccount).Currency
                let validatedAddress = this.ValidateAddress currency destinationAddress.Text
                match validatedAddress with
                | None -> ()
                | Some(destinationAddress) ->
                    let txMetadataWithFeeEstimation = Account.EstimateFee account amount destinationAddress
                    let feeAskMsg = sprintf "Estimated fee for this transaction would be: %s %s"
                                          (txMetadataWithFeeEstimation.FeeValue |> ShowDecimalForHumans CurrencyType.Crypto)
                                          (currency.ToString())
                    let task = this.DisplayAlert("Alert", feeAskMsg, "OK", "Cancel")

                    // FIXME: allow user to specify fiat and/or allbalance
                    let maybeCachedBalance = Account.GetBalance account
                    match maybeCachedBalance with
                    | Fresh(balance) ->
                        let transferAmount = TransferAmount(amount, balance - amount)
                        let txInfo = { Account = account;
                                       Metadata = txMetadataWithFeeEstimation;
                                       Amount = transferAmount;
                                       Destination = destinationAddress;
                                       Passphrase = passphrase.Text; }
                        task.ContinueWith(this.AnswerToFee txInfo) |> ignore
                    | NotFresh(_) ->
                        this.DisplayAlert("Alert", "No internet connection seems available", "OK") |> ignore
