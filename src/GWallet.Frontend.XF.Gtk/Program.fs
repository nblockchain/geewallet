namespace GWallet.Frontend.XF.Gtk

open System

open Xamarin.Forms
open Xamarin.Forms.Platform.GTK

open GWallet.Backend.FSharpUtil.UwpHacks

module AppSingleton =
    let internal Instance = GWallet.Frontend.XF.App ()

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
        use window = new FormsWindow()
        window.LoadApplication AppSingleton.Instance
        window.SetApplicationTitle "geewallet"
        let snapEnvVar = Environment.GetEnvironmentVariable "SNAP"
        let logoFileName = "logo.png"
        if not (String.IsNullOrEmpty snapEnvVar) then
            window.SetApplicationIcon (SPrintF2 "%s/lib/geewallet/%s" (snapEnvVar.TrimEnd('/')) logoFileName)
        else
            window.SetApplicationIcon logoFileName
        window.SetDefaultSize (1000, 1000)
        window.Show()
        Gtk.Application.Run()
        0