#if !XAMARIN
namespace GWallet.Frontend.Maui
#else
namespace GWallet.Frontend.XF
#endif

#if !XAMARIN
open Microsoft.Maui.Controls
#else
open Xamarin.Forms
#endif

type GlobalState() =

    let resumed = Event<unit>()
    let goneToSleep = Event<unit>()

    member internal __.FireResumed() =
        resumed.Trigger()
    member internal __.FireGoneToSleep() =
        goneToSleep.Trigger()

    interface FrontendHelpers.IGlobalAppState with
        [<CLIEvent>]
        member __.Resumed
            with get() = resumed.Publish
        [<CLIEvent>]
        member __.GoneToSleep
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
