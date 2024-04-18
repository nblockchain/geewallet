namespace GWallet.Frontend.XF.Android

open System

open Android.App
open Android.Content
open Android.Content.PM
open Android.Runtime
open Android.Views
open Android.Widget
open Android.OS
open Xamarin.Forms.Platform.Android

type Resources = GWallet.Frontend.XF.Android.Resource

[<Activity (LaunchMode = LaunchMode.SingleTask, 
            Label = GWallet.Backend.Config.AppName,
            Icon = "@drawable/icon", 
            Theme = "@style/MyTheme", 
            MainLauncher = true, 
            ConfigurationChanges = (ConfigChanges.ScreenSize ||| ConfigChanges.Orientation))>]
type MainActivity() =
    inherit FormsAppCompatActivity()

    override this.OnRequestPermissionsResult(requestCode: int, permissions: string[], grantResults: Permission[]) =
        Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults)

    override this.OnCreate (bundle: Bundle) =
        FormsAppCompatActivity.TabLayoutResource <- Resources.Layout.Tabbar
        FormsAppCompatActivity.ToolbarResource <- Resources.Layout.Toolbar

        base.OnCreate (bundle)
        Xamarin.Forms.Forms.Init (this, bundle)

        Xamarin.Essentials.Platform.Init(this, bundle)
        ZXing.Net.Mobile.Forms.Android.Platform.Init()

        this.LoadApplication (new GWallet.Frontend.XF.App ())
