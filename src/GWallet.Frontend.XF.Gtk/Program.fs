namespace GWallet.Frontend.XF.Gtk

open System

open Xamarin.Forms
open Xamarin.Forms.Platform.GTK

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks
open GWallet.Frontend.XF

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
        window.SetApplicationTitle Config.AppName
        let snapEnvVar = Environment.GetEnvironmentVariable "SNAP"
        let logoFileName = "logo.png"
        if not (String.IsNullOrEmpty snapEnvVar) then
            window.SetApplicationIcon (
                SPrintF3 "%s/lib/%s/%s"
                    (snapEnvVar.TrimEnd('/'))
                    Config.AppName
                    logoFileName
            )
        else
            window.SetApplicationIcon logoFileName
        window.SetDefaultSize FrontendHelpers.DefaultDesktopWindowSize
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
            Console.WriteLine (
                SPrintF2 "%s v%s"
                    Config.AppName
                    VersionHelper.CURRENT_VERSION
            )
            0
        | 1 when argv.[0] = "--console" ->
            GWallet.Frontend.Console.Program.Main Array.empty
        | _ ->
            failwith "Arguments not recognized"
