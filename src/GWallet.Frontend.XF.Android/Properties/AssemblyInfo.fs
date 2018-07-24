namespace GWallet.Frontend.XF.Android.Properties

open System.Reflection
open System.Runtime.CompilerServices

// see https://github.com/xamarin/Xamarin.Forms/issues/3329 and https://github.com/mono/monodevelop/issues/5452
[<assembly: Android.Runtime.ResourceDesigner("GWallet.Frontend.XF.Android.Resources", IsApplication=true)>]

[<assembly: AssemblyTitle("GWallet.Frontend.XF.Android")>]
[<assembly: AssemblyDescription("")>]
[<assembly: AssemblyConfiguration("")>]
[<assembly: AssemblyCompany("")>]
[<assembly: AssemblyProduct("")>]
[<assembly: AssemblyCopyright("${AuthorCopyright}")>]
[<assembly: AssemblyTrademark("")>]

// The assembly version has the format {Major}.{Minor}.{Build}.{Revision}

[<assembly: AssemblyVersion("1.0.0.0")>]

//[<assembly: AssemblyDelaySign(false)>]
//[<assembly: AssemblyKeyFile("")>]

#if DEBUG
[<assembly: Android.App.Application(Debuggable=true)>]
#else
[<assembly: Android.App.Application(Debuggable=false)>]
#endif

()
