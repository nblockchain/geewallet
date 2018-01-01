namespace GWallet.Backend

    type TransferAmount(valueToSend: decimal, idealValueRemainingAfterSending: decimal) =
        do
            if valueToSend <= 0m then
                invalidArg "valueToSend" "Amount has to be above zero"
            if idealValueRemainingAfterSending < 0m then
                invalidArg "idealValueRemainingAfterSending" "Amount has to be non-negative"

        member this.ValueToSend = valueToSend

        // "Ideal" prefix means: in an ideal world in which fees don't exist
        member this.IdealValueRemainingAfterSending = idealValueRemainingAfterSending