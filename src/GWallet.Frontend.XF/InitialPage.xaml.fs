namespace GWallet.Frontend.XF

open System
open System.Linq

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Frontend.XF.Controls

type InitialPage() =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<InitialPage>)

    let chartView = base.FindByName<CircleChartView> "normalChartView"

    let RedrawCircleView () =
        chartView.SegmentsSource <- [ { Color = Color.Black ; Percentage = 100.} ]

    do
        Device.BeginInvokeOnMainThread(fun _ ->
            RedrawCircleView ()
        )

    member this.OnNextClicked(_sender: Object, _args: EventArgs) =
        Device.BeginInvokeOnMainThread(fun _ ->
            let secondPage () =
                SecondPage () :> Page
            FrontendHelpers.SwitchToNewPage this secondPage true
        )


