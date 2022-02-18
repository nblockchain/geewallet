namespace GWallet.Frontend.XF

open System
open System.Linq

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Frontend.XF.Controls

type BalancesPage() =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<BalancesPage>)

    let chartView = base.FindByName<CircleChartView> "normalChartView"

    let RedrawCircleView () =
        chartView.SegmentsSource <- [ { Color = Color.Black ; Percentage = 100.} ]

    do
        Device.BeginInvokeOnMainThread(fun _ ->
            RedrawCircleView ()
        )

    member this.OnNextClicked(_sender: Object, _args: EventArgs) =
        Device.BeginInvokeOnMainThread(fun _ ->
            let receivePage () =
                ReceivePage() :> Page
            FrontendHelpers.SwitchToNewPage this receivePage true
        )


