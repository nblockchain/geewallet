#!/usr/bin/env bash
set -e

# TODO: check for .NET SDK in the configure.fsx file as well

# taken from https://www.microsoft.com/net/download/linux-package-manager/ubuntu18-04/sdk-current
apt install -y wget
wget -q https://packages.microsoft.com/config/ubuntu/18.04/packages-microsoft-prod.deb
dpkg -i packages-microsoft-prod.deb
apt install -y apt-transport-https
apt update

apt install -y dotnet-runtime-2.1 aspnetcore-runtime-2.1

# HACK to get old version of dotnetsdk, otherwise `apt install -y dotnet-sdk-2.1` would
# install a buggy version that can't build .NET Standard, see https://github.com/dotnet/core/issues/2460 and https://github.com/NuGet/Home/issues/7956
SDK_PKG=dotnet-sdk-2.1.505-x64.deb
curl https://packages.microsoft.com/ubuntu/18.04/prod/pool/main/d/dotnet-sdk-2.1/$SDK_PKG -O
dpkg --install $SDK_PKG

dotnet --version
