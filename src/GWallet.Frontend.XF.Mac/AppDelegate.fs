namespace GWallet.Frontend.XF.Mac

open Foundation
open AppKit

open Xamarin.Forms
open Xamarin.Forms.Platform.MacOS

module AppSingleton =
    do
        Xamarin.Forms.Forms.Init()

    let internal Instance = GWallet.Frontend.XF.App ()

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

        this.LoadApplication AppSingleton.Instance

        base.DidFinishLaunching(notification)