namespace GWallet.Frontend.XF

open System

type GlobalState() =

    let resumed = new Event<unit>()
    let goneToSleep = new Event<unit>()

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