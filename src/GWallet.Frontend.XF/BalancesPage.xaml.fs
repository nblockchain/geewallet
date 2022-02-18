namespace GWallet.Frontend.XF

open System
open System.Linq

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Frontend.XF.Controls
open GWallet.Backend

type BalancesPage(normalBalanceStates: seq<BalanceState>)
                      as this =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<BalancesPage>)

    let contentLayout = base.FindByName<StackLayout> "contentLayout"
    let normalChartView = base.FindByName<CircleChartView> "normalChartView"

    let rec FindCryptoBalances (cryptoBalanceClassId: string) (layout: StackLayout) 
                               (elements: List<View>) (resultsSoFar: List<Frame>): List<Frame> =
        match elements with
        | [] -> resultsSoFar
        | head::tail ->
            match head with
            | :? Frame as frame ->
                let newResults =
                    if frame.ClassId = cryptoBalanceClassId then
                        frame::resultsSoFar
                    else
                        resultsSoFar
                FindCryptoBalances cryptoBalanceClassId layout tail newResults
            | _ ->
                FindCryptoBalances cryptoBalanceClassId layout tail resultsSoFar

    let GetAmountOrDefault maybeAmount =
        match maybeAmount with
        | NotFresh NotAvailable ->
            0m
        | Fresh amount | NotFresh (Cached (amount,_)) ->
            amount

    let RedrawCircleView (balances: seq<BalanceState>) =
        let chartView = normalChartView
        let fullAmount = balances.Sum(fun b -> GetAmountOrDefault b.FiatAmount)

        let chartSourceList = 
            balances |> Seq.map (fun balanceState ->
                 let percentage = 
                     if fullAmount = 0m then
                         0m
                     else
                         GetAmountOrDefault balanceState.FiatAmount / fullAmount
                 { 
                     Color = FrontendHelpers.GetCryptoColor balanceState.BalanceSet.Account.Currency
                     Percentage = float(percentage)
                 }
            )
        chartView.SegmentsSource <- chartSourceList

    do
        this.Init()

    member this.PopulateBalances (readOnly: bool) (balances: seq<BalanceState>) =
        let activeCurrencyClassId,inactiveCurrencyClassId =
            FrontendHelpers.GetActiveAndInactiveCurrencyClassIds readOnly

        let contentLayoutChildrenList = (contentLayout.Children |> List.ofSeq)

        let activeCryptoBalances = FindCryptoBalances activeCurrencyClassId 
                                                      contentLayout 
                                                      contentLayoutChildrenList
                                                      List.Empty

        let inactiveCryptoBalances = FindCryptoBalances inactiveCurrencyClassId 
                                                        contentLayout 
                                                        contentLayoutChildrenList
                                                        List.Empty

        contentLayout.BatchBegin()                      

        for inactiveCryptoBalance in inactiveCryptoBalances do
            inactiveCryptoBalance.IsVisible <- false

        //We should create new frames only once, then just play with IsVisible(True|False) 
        if activeCryptoBalances.Any() then
            for activeCryptoBalance in activeCryptoBalances do
                activeCryptoBalance.IsVisible <- true
        else
            for balanceState in balances do
                let balanceSet = balanceState.BalanceSet
                let tapGestureRecognizer = TapGestureRecognizer()
                tapGestureRecognizer.Tapped.Subscribe(fun _ ->
                    let receivePage () =
                        ReceivePage() :> Page
                    FrontendHelpers.SwitchToNewPage this receivePage true
                ) |> ignore
                let frame = balanceSet.Widgets.Frame
                frame.GestureRecognizers.Add tapGestureRecognizer
                contentLayout.Children.Add frame

        contentLayout.BatchCommit()

    member this.PopulateGridInitially () =
        this.PopulateBalances false normalBalanceStates
        RedrawCircleView normalBalanceStates

    member private this.Init () =
        normalChartView.DefaultImageSource <- FrontendHelpers.GetSizedImageSource "logo" 512

        Device.BeginInvokeOnMainThread(fun _ ->
            this.PopulateGridInitially ()
        )

