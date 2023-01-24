
namespace GWallet

module VersionInfo =
    [<Literal>]
    let VersionString = "0.4.367.0"

open System.Reflection

[<assembly: AssemblyCompany("NBlockchain")>]
[<assembly: AssemblyProduct("GWallet")>]
[<assembly: AssemblyCopyright("Copyright ©  2017")>]

// Version information for an assembly consists of the following four values:
//
//       Major Version
//       Minor Version
//       Build Number
//       Revision
//
// You can specify all the values or you can default the Build and Revision Numbers
// by using the '*' as shown below:
// [<assembly: AssemblyVersion("1.0.*")>]

[<assembly: AssemblyVersion(VersionInfo.VersionString)>]
[<assembly: AssemblyFileVersion(VersionInfo.VersionString)>]

do
    ()
