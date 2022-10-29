#!/usr/bin/env fsharpi

open System
open System.IO

#r "System.Configuration"
open System.Configuration
#load "fsx/InfraLib/Misc.fs"
#load "fsx/InfraLib/Process.fs"
#load "fsx/InfraLib/Unix.fs"
open FSX.Infrastructure
open Process

let binFolderName = "bin"
let outputSubFolder = "apt"
let outputFolder = Path.Combine(binFolderName, outputSubFolder)
let packageDependencies = "fsharp"::("libgtk2.0-cil-dev"::[])

let currentDir = Directory.GetCurrentDirectory()
let binAptDir = Path.Combine(currentDir, outputFolder)
if not (Directory.Exists binAptDir) then
    Directory.CreateDirectory binAptDir |> ignore
Directory.SetCurrentDirectory binAptDir

let installScriptTemplate = """#!/usr/bin/env bash
set -euo pipefail

if [ "$EUID" -ne 0 ]; then
    echo "Please use sudo"
    exit 1
fi

APT_SOURCE_FILENAME=/etc/apt/sources.list.d/apt-{aptSourceName}-offline.list
DIR_OF_THIS_SCRIPT=$(dirname $(readlink -f $0))
echo "deb file:///$DIR_OF_THIS_SCRIPT/ ./" > $APT_SOURCE_FILENAME
apt update --allow-insecure-repositories --allow-unauthenticated
apt install -y --allow-unauthenticated {dep}
rm $APT_SOURCE_FILENAME
apt update
"""

let installScriptContents =
    installScriptTemplate.Replace("{aptSourceName}", "geewalletdependencies")
let installScriptFileName = "install.sh"

try
    Unix.DownloadAptPackagesRecursively packageDependencies
    Unix.InstallAptPackageIfNotAlreadyInstalled "dpkg-dev"
    let scanPkg =
        Process.Execute(
            { Command = "dpkg-scanpackages"; Arguments = "." },
            Echo.Off
        )
    File.WriteAllText ("Packages", scanPkg.UnwrapDefault())
    File.WriteAllText (installScriptFileName, installScriptContents)
    Process.Execute(
        { Command = "chmod"
          Arguments = sprintf "ugo+x %s" installScriptFileName },
        Echo.All
    ).UnwrapDefault() |> ignore<string>
finally
    Directory.SetCurrentDirectory currentDir

Unix.InstallAptPackageIfNotAlreadyInstalled "zip"

let binDir = Path.Combine(currentDir, binFolderName)
Directory.SetCurrentDirectory binDir
let zipNameWithoutExtension = outputSubFolder
Process.Execute(
    { Command = "zip"
      Arguments = sprintf "-r %s.zip %s" zipNameWithoutExtension outputSubFolder },
    Echo.All
).UnwrapDefault() |> ignore<string>

Console.WriteLine ()
Console.WriteLine (sprintf "Success. All your files are in the '%s' folder and inside the '%s/%s.zip' file."
                           outputFolder binFolderName zipNameWithoutExtension)
