#!/usr/bin/env bash
set -euxo pipefail

# Install gtk workload and add Maui nuget source
./scripts/prepare_maui_gtk.sh

# Build GWallet MAUI Gtk project
./configure.sh
make
dotnet build --configuration=Release --framework=net6.0-gtk ./src/GWallet.Frontend.Maui/GWallet.Frontend.Maui.fsproj
dotnet publish --configuration=Release --framework=net6.0-gtk --output=./staging ./src/GWallet.Frontend.Maui/GWallet.Frontend.Maui.fsproj
cp ./scripts/geewallet-maui-gtk.sh ./staging/

rm ./snap/snapcraft.yaml
mv ./snap/local/snapcraft_maui.yaml ./snap/snapcraft.yaml

sudo snapcraft --destructive-mode
