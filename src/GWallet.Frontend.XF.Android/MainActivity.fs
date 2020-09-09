namespace GWallet.Frontend.XF.Android

open System

open Android.App
open Android.Content.PM
open Android.OS

open Xamarin.Forms.Platform.Android

type Resources = GWallet.Frontend.XF.Android.Resource

module AppSingleton =
    let internal Instance = GWallet.Frontend.XF.App ()

[<Activity (LaunchMode = LaunchMode.SingleTask, 
            Label = "geewallet", 
            Icon = "@drawable/icon", 
            Theme = "@style/MyTheme", 
            MainLauncher = true, 
            ConfigurationChanges = (ConfigChanges.ScreenSize ||| ConfigChanges.Orientation))>]
type MainActivity() =
    inherit FormsAppCompatActivity()


    override this.OnRequestPermissionsResult(requestCode: int, permissions: string[], grantResults: Permission[]) =
        ZXing.Net.Mobile.Android.PermissionsHandler.OnRequestPermissionsResult(requestCode, permissions, grantResults)

    override this.OnCreate (bundle: Bundle) =
        FormsAppCompatActivity.TabLayoutResource <- Resources.Layout.Tabbar
        FormsAppCompatActivity.ToolbarResource <- Resources.Layout.Toolbar

        base.OnCreate (bundle)

        Xamarin.Forms.Forms.Init (this, bundle)

        ZXing.Net.Mobile.Forms.Android.Platform.Init()

        this.LoadApplication AppSingleton.Instance
