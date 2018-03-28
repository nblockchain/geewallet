namespace GWalletFrontendXamForms.Droid
open System

open Android.App
open Android.Content
open Android.Content.PM
open Android.Runtime
open Android.Views
open Android.Widget
open Android.OS

open ZXing.Net.Mobile.Forms.Android

[<Activity (Label = "GWallet", Icon = "@drawable/icon", MainLauncher = true, ConfigurationChanges = (ConfigChanges.ScreenSize ||| ConfigChanges.Orientation))>]
type MainActivity() =
    inherit Xamarin.Forms.Platform.Android.FormsApplicationActivity()

    override this.OnRequestPermissionsResult(requestCode: int, permissions: string[], grantResults: Permission[]) =
        ZXing.Net.Mobile.Android.PermissionsHandler.OnRequestPermissionsResult(requestCode, permissions, grantResults)

    override this.OnCreate (bundle: Bundle) =
        base.OnCreate (bundle)

        Xamarin.Forms.Forms.Init (this, bundle)

        ZXing.Net.Mobile.Forms.Android.Platform.Init()

        this.LoadApplication (new GWallet.Frontend.XF.App ())

        // workaround for bug https://github.com/xamarin/Xamarin.Forms/issues/2203
        this.ActionBar.SetIcon(int Android.Graphics.Color.Transparent)

