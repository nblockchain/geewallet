namespace GWalletFrontendXamForms

open Xamarin.Forms
open Xamarin.Forms.Xaml

type GWalletFrontendXamFormsPage() = 
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<GWalletFrontendXamFormsPage>)
