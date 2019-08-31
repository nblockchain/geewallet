@ECHO OFF
.nuget\nuget.exe install NUnit.Runners -Version 2.7.1
NUnit.Runners.2.7.1\tools\nunit-console.exe src\GWallet.Backend.Tests\bin\GWallet.Backend.Tests.dll
