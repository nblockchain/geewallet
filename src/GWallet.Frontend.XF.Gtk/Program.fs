namespace GWallet.Frontend.XF.Gtk

open System

open Xamarin.Forms
open Xamarin.Forms.Platform.GTK

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks

module Main =

    let NormalStartWithNoParameters() =
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
            window.SetApplicationIcon (SPrintF2 "%s/lib/geewallet/%s" (snapEnvVar.TrimEnd('/')) logoFileName)
        else
            window.SetApplicationIcon logoFileName
        window.SetDefaultSize (500, 1000)
        window.Show()
        Gtk.Application.Run()
        0

    [<EntryPoint>]
    [<STAThread>]
    let main argv =
        match argv.Length with
        | 0 ->
            NormalStartWithNoParameters()
        | 1 when argv.[0] = "--version" ->
            Console.WriteLine (SPrintF1 "geewallet v%s" VersionHelper.CURRENT_VERSION)
            0
        | 1 when argv.[0] = "--console" ->
            GWallet.Frontend.Console.Program.Main Array.empty
        | _ ->
            failwith "Arguments not recognized"
