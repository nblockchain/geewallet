#!/usr/bin/env bash
set -euxo pipefail

# Detect Ubuntu version
source /etc/os-release
UBUNTU_VERSION=$VERSION_ID

# required by curl and gpg
apt install --yes curl gnupg2 dirmngr ca-certificates

# taken from http://www.mono-project.com/download/stable/#download-lin
curl -fsSL "https://keyserver.ubuntu.com/pks/lookup?op=get&search=0x3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF" | gpg --dearmor | tee /usr/share/keyrings/mono-official-archive-keyring.gpg > /dev/null
echo "deb [signed-by=/usr/share/keyrings/mono-official-archive-keyring.gpg] https://download.mono-project.com/repo/ubuntu stable-focal main" | tee /etc/apt/sources.list.d/mono-official-stable.list

if [[ "$UBUNTU_VERSION" == "26.04" ]]; then
    # NOTE: pin the Microsoft repo above the Ubuntu archive for the mono packages:
    # 26.04 ships a higher-versioned mono (6.14) which doesn't include the
    # mono-gac/mono-xbuild packages anymore, so without this pin 'apt install
    # mono-devel' would resolve to the (incomplete) one from the 26.04 archive
    # instead of the one from this repo, and the packages from both sources cannot
    # be mixed together either.
    #
    # NOTE: don't include 'libgdiplus' in the pin: the one from this repo depends
    # on 'libtiff5' which doesn't exist in 26.04 anymore, but the one from the
    # 26.04 archive (6.1+dfsg) is a higher version anyway and satisfies the
    # 'libgdiplus (>= 2.6.7)' dependency of 'libmono-system-drawing4.0-cil'.
    tee /etc/apt/preferences.d/mono-official-stable <<EOF
Package: mono* libmono* libfsharp* fsharp msbuild* ca-certificates-mono cli-common*
Pin: origin download.mono-project.com
Pin-Priority: 1001
EOF
fi

apt update --yes

DEBIAN_FRONTEND=noninteractive apt install --yes ca-certificates-mono mono-devel

# NOTE: do *NOT* join again the apt installation of 'fsharp' with the rest of
# the mono packages, because they have to be installed *before* the 'fsharp' package:
# 'mono-gac' (pulled in by 'mono-devel' from the repo above) runs a postinst
# hook that copies the F# framework files (FSharp.Core.dll, FSharp.Build.dll,
# Microsoft.FSharp.Targets, fsc.exe, fsi.exe...) into /usr/lib/mono/4.5/ (the
# cli-common framework install), without which xbuild cannot build the F#
# projects (and 'fsharpi' is broken, too). Therefore we don't install 'fsharp'
# in the same apt transaction as the mono packages, unlike what the script for
# older Ubuntu versions does.
DEBIAN_FRONTEND=noninteractive apt install --yes fsharp

mono --version
