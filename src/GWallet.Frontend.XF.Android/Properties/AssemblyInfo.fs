namespace GWallet.Frontend.XF.Android.Properties

open System.Reflection
open System.Runtime.CompilerServices

// see https://github.com/xamarin/Xamarin.Forms/issues/3329 and https://github.com/mono/monodevelop/issues/5452
[<assembly: Android.Runtime.ResourceDesigner("GWallet.Frontend.XF.Android.Resources", IsApplication=true)>]

[<assembly: AssemblyTitle("GWallet.Frontend.XF.Android")>]
[<assembly: AssemblyDescription("")>]
[<assembly: AssemblyConfiguration("")>]
[<assembly: AssemblyTrademark("")>]

//[<assembly: AssemblyDelaySign(false)>]
//[<assembly: AssemblyKeyFile("")>]

#if DEBUG
[<assembly: Android.App.Application(Debuggable=true)>]
#else
[<assembly: Android.App.Application(Debuggable=false)>]
#endif

()
