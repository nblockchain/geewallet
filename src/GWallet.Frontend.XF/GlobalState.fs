namespace GWallet.Frontend.XF

open System

open Xamarin.Forms

type GlobalState() =

    let resumed = Event<unit>()
    let goneToSleep = Event<unit>()

    let lockObject = Object()
    let mutable awake = true
    member internal this.Awake
        with set value = lock lockObject (fun _ -> awake <- value)

    member internal this.FireResumed() =
        resumed.Trigger()
    member internal this.FireGoneToSleep() =
        goneToSleep.Trigger()

    interface FrontendHelpers.IGlobalAppState with
        member this.Awake
            with get() = lock lockObject (fun _ -> awake)

        [<CLIEvent>]
        member this.Resumed
            with get() = resumed.Publish
        [<CLIEvent>]
        member this.GoneToSleep
            with get() = goneToSleep.Publish


module DummyPageConstructorHelper =

    [<Literal>]
    let Warning =
        "DO NOT USE THIS! This paramaterless constructor is only here to allow the VS designer to render page"

    let GlobalFuncToRaiseExceptionIfUsedAtRuntime(): FrontendHelpers.IGlobalAppState =
#if !DEBUG // if we put the failwith in DEBUG mode, then the VS designer crashes with it when trying to render
        failwith Warning
#endif
        GlobalState() :> FrontendHelpers.IGlobalAppState

    let PageFuncToRaiseExceptionIfUsedAtRuntime(): Page =
#if !DEBUG // if we put the failwith in DEBUG mode, then the VS designer crashes with it when trying to render
        failwith Warning
#endif
        Page()
