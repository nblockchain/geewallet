namespace GWallet.Frontend.XF.iOS

open UIKit
open Foundation

open Xamarin.Forms
open Xamarin.Forms.Platform.iOS

module AppSingleton =
    do
        Xamarin.Forms.Forms.Init()

    let internal Instance = GWallet.Frontend.XF.App ()

[<Register ("AppDelegate")>]
type AppDelegate () =
    inherit FormsApplicationDelegate ()

    override this.FinishedLaunching (app, options) =
        Forms.Init()

        ZXing.Net.Mobile.Forms.iOS.Platform.Init()

        this.LoadApplication AppSingleton.Instance
        base.FinishedLaunching(app, options)

module Main =
    [<EntryPoint>]
    let main args =
        UIApplication.Main(args, null, "AppDelegate")
        0
