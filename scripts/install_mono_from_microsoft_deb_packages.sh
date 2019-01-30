#!/usr/bin/env bash
set -e

# required by apt-key
apt install -y gnupg2
# required by apt-update when pulling from mono-project.com
apt install -y ca-certificates

# taken from http://www.mono-project.com/download/stable/#download-lin
apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF
echo "deb https://download.mono-project.com/repo/ubuntu stable-bionic main" | tee /etc/apt/sources.list.d/mono-official-stable.list
apt update
apt install -y mono-devel fsharp
mono --version
