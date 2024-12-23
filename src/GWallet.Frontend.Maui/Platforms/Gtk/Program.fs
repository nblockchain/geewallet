namespace GWallet.Frontend.Maui

open System
open System.Threading.Tasks
open GLib
open Microsoft.Extensions.Hosting
open Microsoft.Maui
open Microsoft.Maui.Hosting

module Program = 
    [<EntryPoint>]
    let main args = 
        match args with
        | [||] ->
            let app = GtkApp()
            app.Run()
            0
        | [| "--version" |] ->
            printfn
                "%s v%s"
                GWallet.Backend.Config.AppName
                GWallet.Backend.VersionHelper.CURRENT_VERSION
            0
        | _ ->
            failwith "Arguments not recognized"
