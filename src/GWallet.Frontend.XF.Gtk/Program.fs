namespace GWallet.Frontend.XF.Gtk

open System

open Xamarin.Forms
open Xamarin.Forms.Platform.GTK

module Main =

    [<EntryPoint>]
    [<STAThread>]
    let main argv =
        Gtk.Application.Init()
        Forms.Init()

        // TODO: detect Windows/UWP too
        if GWallet.Backend.Config.IsMacPlatform() then
            failwith "The GTK frontend is only officially supported for the Linux OS"

        ZXing.Net.Mobile.Forms.GTK.Platform.Init()
        let app = GWallet.Frontend.XF.App()
        use window = new FormsWindow()
        window.LoadApplication(app)
        window.SetApplicationTitle "geewallet"
        let snapEnvVar = Environment.GetEnvironmentVariable "SNAP"
        let logoFileName = "logo.png"
        if not (String.IsNullOrEmpty snapEnvVar) then
            window.SetApplicationIcon (sprintf "%s/lib/geewallet/%s" (snapEnvVar.TrimEnd('/')) logoFileName)
        else
            window.SetApplicationIcon logoFileName
        window.SetDefaultSize (1000, 1000)
        window.Show()
        Gtk.Application.Run()
        0