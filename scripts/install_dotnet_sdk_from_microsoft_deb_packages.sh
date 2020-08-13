#!/usr/bin/env bash
set -e

# TODO: check for .NET SDK in the configure.fsx file as well

# taken from https://docs.microsoft.com/en-gb/dotnet/core/install/linux-ubuntu#2004-
apt install -y wget
wget -q https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb
dpkg -i packages-microsoft-prod.deb
apt install -y apt-transport-https
apt update

apt-get install -y dotnet-sdk-2.1

dotnet --version
