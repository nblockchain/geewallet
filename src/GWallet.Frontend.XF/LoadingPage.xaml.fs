namespace GWallet.Frontend.XF

open System
open System.Linq

open Xamarin.Forms
open Xamarin.Forms.Xaml

open GWallet.Backend

type LoadingPage(state: FrontendHelpers.IGlobalAppState) as this =
    inherit ContentPage()

    let _ = base.LoadFromXaml(typeof<LoadingPage>)

    let allAccounts = Account.GetAllActiveAccounts()
    let normalAccounts = allAccounts.OfType<NormalAccount>() |> List.ofSeq
                         |> List.map (fun account -> account :> IAccount)
    let readOnlyAccounts = allAccounts.OfType<ReadOnlyAccount>() |> List.ofSeq
                           |> List.map (fun account -> account :> IAccount)

    do
        this.Init()

    [<Obsolete(DummyPageConstructorHelper.Warning)>]
    new() = LoadingPage(DummyPageConstructorHelper.GlobalFuncToRaiseExceptionIfUsedAtRuntime())

    member this.Init (): unit =

        let normalAccountsWithLabels = FrontendHelpers.CreateWidgetsForAccounts normalAccounts
        let allNormalAccountBalancesJob =
            seq {
                for balanceSet in normalAccountsWithLabels do
                    let balanceJob =
                        FrontendHelpers.UpdateBalanceAsync balanceSet
                    yield balanceJob
            } |> Async.Parallel

        let readOnlyAccountsBalances = FrontendHelpers.CreateWidgetsForAccounts readOnlyAccounts
        let readOnlyAccountBalancesJob = FrontendHelpers.UpdateCachedBalancesAsync readOnlyAccountsBalances

        let populateGrid = async {
            let allBalancesJob = Async.Parallel(allNormalAccountBalancesJob::(readOnlyAccountBalancesJob::List.Empty))
            let! allResolvedBalances = allBalancesJob
            let allResolvedNormalAccountBalances = allResolvedBalances.ElementAt(0)
            let allResolvedReadOnlyBalances = allResolvedBalances.ElementAt(1)

            Device.BeginInvokeOnMainThread(fun _ ->
                let balancesPage = BalancesPage(state, allResolvedNormalAccountBalances, allResolvedReadOnlyBalances, false)
                FrontendHelpers.SwitchToNewPageDiscardingCurrentOne this balancesPage
            )
        }
        Async.StartAsTask populateGrid
            |> FrontendHelpers.DoubleCheckCompletion

        ()

