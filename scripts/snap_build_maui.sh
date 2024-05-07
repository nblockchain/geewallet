#!/usr/bin/env bash
set -euxo pipefail

# Install gtk workload
wget -O gtksharp.net.sdk.gtk.manifest-$DotnetVersion.nupkg https://globalcdn.nuget.org/packages/gtksharp.net.sdk.gtk.manifest-$DotnetVersion.$GtkSharpVersion.nupkg
DOTNET_DIR=~/.dotnet
WORKLOAD_MANIFEST_DIR=$DOTNET_DIR/sdk-manifests/$DotnetVersion/gtksharp.net.sdk.gtk
mkdir -p $WORKLOAD_MANIFEST_DIR/
unzip -j gtksharp.net.sdk.gtk.manifest-$DotnetVersion.nupkg "data/*" -d $WORKLOAD_MANIFEST_DIR/
rm gtksharp.net.sdk.gtk.manifest-$DotnetVersion.nupkg
# otherwise we get System.UnauthorizedAccessException: Access to the path '/home/runner/.dotnet/sdk-manifests/6.0.300/gtksharp.net.sdk.gtk/WorkloadManifest.json' is denied.
chmod 764 $WORKLOAD_MANIFEST_DIR/*
dotnet workload search
dotnet workload install gtk --skip-manifest-update

#Add Maui Nuget source
cd dependencies/maui
dotnet nuget add source --name nuget https://api.nuget.org/v3/index.json
cd ../..

# Build GWallet MAUI Gtk project
./configure.sh
make
dotnet build --configuration=Release --framework=net6.0-gtk ./src/GWallet.Frontend.Maui/GWallet.Frontend.Maui.fsproj
dotnet publish --configuration=Release --framework=net6.0-gtk --output=./staging ./src/GWallet.Frontend.Maui/GWallet.Frontend.Maui.fsproj

rm ./snap/snapcraft.yaml
mv ./snap/local/snapcraft_maui.yaml ./snap/snapcraft.yaml

# Install snap packages needed to build geewallet package in
# separate command under sudo. This way snapcraft command can be
# run as regular user and not cause troubles with dotnet workflows.
sudo snap install --channel=latest/stable core22

snapcraft --destructive-mode
