namespace GWallet.Frontend.XF.iOS

open System
open UIKit
open Foundation
open Xamarin.Forms
open Xamarin.Forms.Platform.iOS

[<Register ("AppDelegate")>]
type AppDelegate () =
    inherit FormsApplicationDelegate ()

    override this.FinishedLaunching (app, options) =
        Forms.Init()

        ZXing.Net.Mobile.Forms.iOS.Platform.Init()

        this.LoadApplication (new GWallet.Frontend.XF.App())
        base.FinishedLaunchingg(app, options)

module Main =
    [<EntryPoint>]
    let main args =
        UIApplication.Main(args, null, "AppDelegate")
        0
