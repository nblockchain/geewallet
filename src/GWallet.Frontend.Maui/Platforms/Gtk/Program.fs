namespace GWallet.Frontend.Maui

open System
open System.Threading.Tasks
open GLib
open Microsoft.Extensions.Hosting
open Microsoft.Maui
open Microsoft.Maui.Hosting

module Program = 
    [<EntryPoint>]
    let main _args = 
        let app = GtkApp()
        app.Run()
        0
