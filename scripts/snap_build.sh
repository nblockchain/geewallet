#!/usr/bin/env bash
set -euxo pipefail

# hack to disable dotnet detection (can't use apt purge because github VM seems to have it installed in different way)
sudo rm `which dotnet`

# this is the equivalent of using the 'build-packages' (not stage-packages) section in snapcraft
# but as we're not using the 'make' plugin, we need to this manually now
DEBIAN_FRONTEND=noninteractive sudo apt install -y fsharp build-essential pkg-config cli-common-dev mono-devel


./configure.sh --prefix=./staging
make
make install

#this below is to prevent the possible error "Failed to reuse files from previous run: The 'pull' step of 'gwallet' is out of date: The source has changed on disk."
#snapcraft clean gwallet -s pull

snapcraft --destructive-mode
