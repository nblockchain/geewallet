namespace GWallet.Frontend.XF.Mac

open System
open Foundation
open AppKit

open Xamarin.Forms
open Xamarin.Forms.Platform.MacOS

[<Register ("AppDelegate")>]
type AppDelegate () =
    inherit FormsApplicationDelegate ()

    let style = NSWindowStyle.Closable ||| NSWindowStyle.Resizable ||| NSWindowStyle.Titled
    let rect = new CoreGraphics.CGRect(200.0f, 1000.0f, 1024.0f, 768.0f)
    let window = new NSWindow(rect, style, NSBackingStore.Buffered, false)

    do
        window.Title <- "Xamarin.Forms on Mac!"
        window.TitleVisibility <- NSWindowTitleVisibility.Hidden

    override this.MainWindow
        with get() = window

    override this.DidFinishLaunching(notification: NSNotification) =
        Forms.Init()

        ZXing.Net.Mobile.Forms.macOS.Platform.Init()

        this.LoadApplication(new GWallet.Frontend.XF.App())

        base.DidFinishLaunching(notification)