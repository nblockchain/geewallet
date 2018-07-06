namespace GWallet.Frontend.XF

open System
open System.Threading.Tasks

open Xamarin.Forms

open GWallet.Backend

type FrontendHelpers =

    static member internal BigFontSize = 22.

    static member internal MediumFontSize = 20.

    static member internal MagicGtkNumber = 20.

    static member internal ExchangeRateUnreachableMsg = " (~ ? USD)"

    // FIXME: share code between Frontend.Console and Frontend.XF
    // with this we want to avoid the weird default US format of starting with the month, then day, then year... sigh
    static member ShowSaneDate (date: DateTime): string =
        date.ToString("dd-MMM-yyyy")

    // FIXME: add this use case to Formatting module, and with a unit test
    static member ShowDecimalForHumansWithMax (currencyType: CurrencyType, amount: decimal, maxAmount: decimal): string =
        let amountOfDecimalsToShow =
            match currencyType with
            | CurrencyType.Fiat -> 2
            | CurrencyType.Crypto -> 5
        // https://stackoverflow.com/a/25451689/544947
        let truncated = amount - (amount % (1m / decimal(amountOfDecimalsToShow * 10)))
        if (truncated > maxAmount) then
            failwithf "how can %s be higher than %s?" (truncated.ToString()) (maxAmount.ToString())

        Formatting.DecimalAmount currencyType truncated

    // FIXME: share code between Frontend.Console and Frontend.XF
    static member BalanceInUsdString (balance: decimal, maybeUsdValue: MaybeCached<decimal>)
                                     : MaybeCached<decimal>*string =
        match maybeUsdValue with
        | NotFresh(NotAvailable) ->
            NotFresh(NotAvailable),FrontendHelpers.ExchangeRateUnreachableMsg
        | Fresh(usdValue) ->
            let fiatBalance = usdValue * balance
            Fresh(fiatBalance),sprintf "~ %s USD"
                                   (Formatting.DecimalAmount CurrencyType.Fiat fiatBalance)
        | NotFresh(Cached(usdValue,time)) ->
            let fiatBalance = usdValue * balance
            NotFresh(Cached(fiatBalance,time)),sprintf "~ %s USD (as of %s)"
                                                    (Formatting.DecimalAmount CurrencyType.Fiat fiatBalance)
                                                    (time |> FrontendHelpers.ShowSaneDate)

    // when running Task<unit> or Task<T> where we want to ignore the T, we should still make sure there is no exception,
    // & if there is, bring it to the main thread to fail fast, report to Sentry, etc, otherwise it gets ignored
    static member DoubleCheckCompletion<'T> (task: Task<'T>) =
        task.ContinueWith(fun (t: Task<'T>) ->
            if (t.Exception <> null) then
                Device.BeginInvokeOnMainThread(fun _ ->
                    raise(task.Exception)
                )
        ) |> ignore
    static member DoubleCheckCompletion (task: Task) =
        task.ContinueWith(fun (t: Task) ->
            if (t.Exception <> null) then
                Device.BeginInvokeOnMainThread(fun _ ->
                    raise(task.Exception)
                )
        ) |> ignore

    static member DoubleCheckCompletion<'T> (work: Async<'T>): unit =
        async {
            try
                let! _ = work
                ()
            with
            | ex ->
                Device.BeginInvokeOnMainThread(fun _ ->
                    raise(ex)
                )
            return ()
        } |> Async.Start

    static member ChangeTextAndChangeBack (button: Button) (newText: string) =
        let initialText = button.Text
        button.IsEnabled <- false
        button.Text <- newText
        Task.Run(fun _ ->
            Task.Delay(TimeSpan.FromSeconds(2.0)).Wait()
            Device.BeginInvokeOnMainThread(fun _ ->
                button.Text <- initialText
                button.IsEnabled <- true
            )
        ) |> FrontendHelpers.DoubleCheckCompletion

