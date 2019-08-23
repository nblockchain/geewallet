#!/usr/bin/env bash
set -e

# TODO: check for .NET SDK in the configure.fsx file as well

# taken from https://www.microsoft.com/net/download/linux-package-manager/ubuntu18-04/sdk-current
apt install -y wget
wget -q https://packages.microsoft.com/config/ubuntu/18.04/packages-microsoft-prod.deb
dpkg -i packages-microsoft-prod.deb
apt install -y apt-transport-https
apt update

apt-get install -y dotnet-sdk-2.1

dotnet --version
