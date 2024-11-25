#!/usr/bin/env bash
set -euxo pipefail

# Install gtk workload
wget -O gtksharp.net.sdk.gtk.manifest-$DotNetVersionForMauiGtk.nupkg https://globalcdn.nuget.org/packages/gtksharp.net.sdk.gtk.manifest-$DotNetVersionForMauiGtk.$GtkSharpVersion.nupkg
DOTNET_DIR=~/.dotnet
WORKLOAD_MANIFEST_DIR=$DOTNET_DIR/sdk-manifests/$DotNetVersionForMauiGtk/gtksharp.net.sdk.gtk
mkdir -p $WORKLOAD_MANIFEST_DIR/
unzip -j gtksharp.net.sdk.gtk.manifest-$DotNetVersionForMauiGtk.nupkg "data/*" -d $WORKLOAD_MANIFEST_DIR/
rm gtksharp.net.sdk.gtk.manifest-$DotNetVersionForMauiGtk.nupkg
# otherwise we get System.UnauthorizedAccessException: Access to the path '/home/runner/.dotnet/sdk-manifests/6.0.300/gtksharp.net.sdk.gtk/WorkloadManifest.json' is denied.
chmod 764 $WORKLOAD_MANIFEST_DIR/*
dotnet workload search
dotnet workload install gtk --skip-manifest-update

