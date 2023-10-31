#!/usr/bin/env bash
set -euxo pipefail

# this is the equivalent of using the 'build-packages' (not stage-packages) section in snapcraft
# but as we're not using the 'make' plugin, we need to this manually now
DEBIAN_FRONTEND=noninteractive sudo apt install -y fsharp build-essential pkg-config cli-common-dev mono-devel libgtk2.0-cil-dev dotnet6

# just in case this is a retry-run, we want to clean artifacts from previous try
rm -rf ./staging

./configure.sh --prefix=./staging "$@"
make
make install

snapcraft --destructive-mode
