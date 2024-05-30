namespace GWallet.Frontend.XF.Mac

open AppKit

module main =
    [<EntryPoint>]
    let main args =
        NSApplication.Init()
        NSApplication.SharedApplication.Delegate <- new AppDelegatee()
        NSApplication.Main(args)
        0
