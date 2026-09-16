#!/usr/bin/env bash
set -euxo pipefail

# Microsoft's APT repository for 20.04 is same as for 22.04
#source /etc/os-release

# required by curl and gpg
apt install --yes curl gnupg2 dirmngr ca-certificates

# taken from http://www.mono-project.com/download/stable/#download-lin
curl -fsSL "https://keyserver.ubuntu.com/pks/lookup?op=get&search=0x3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF" | gpg --dearmor | tee /usr/share/keyrings/mono-official-archive-keyring.gpg > /dev/null
echo "deb [signed-by=/usr/share/keyrings/mono-official-archive-keyring.gpg] https://download.mono-project.com/repo/ubuntu stable-focal main" | tee /etc/apt/sources.list.d/mono-official-stable.list
apt update
DEBIAN_FRONTEND=noninteractive apt install -y mono-devel fsharp
mono --version
