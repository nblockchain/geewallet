#!/usr/bin/env bash
set -euxo pipefail

BUILD_CONFIG="./scripts/build.config"
if [ ! -f "$BUILD_CONFIG" ]; then
    echo "ERROR: configure hasn't been run yet, run ./configure.sh first" >&2 && exit 1
fi

# to make sure snapcraft is installed, as it will be called
# by snap_release.fsx
snapcraft --version

source "$BUILD_CONFIG"
FsxRunner=$FsxRunner $FsxRunner ./scripts/snap_release.fsx "$@"
