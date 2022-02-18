namespace GWallet.Frontend.XF

open System

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Backend

type ReceivePage(account: IAccount,

                 usdRate: MaybeCached<decimal>,

                 balancesPage: Page,
                 balanceWidgetsFromBalancePage: BalanceWidgets) =

    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<ReceivePage>)

    member this.OnSendPaymentClicked(_sender: Object, _args: EventArgs) =
        let newReceivePageFunc = (fun _ ->
            ReceivePage(account, usdRate, balancesPage, balanceWidgetsFromBalancePage) :> Page
        )
        let sendPage () =
            let newPage = SendPage(account, this, newReceivePageFunc)

            newPage :> Page

        FrontendHelpers.SwitchToNewPage this sendPage false

