#!/usr/bin/env bash
set -euxo pipefail

if [ -e snapcraft.login ]; then
    echo "snapcraft.login found, skipping log-in"
else
    snapcraft export-login snapcraft.login
fi
snapcraft login --with snapcraft.login

if [ ! -e *.snap ]; then
    ./snap_build.sh
fi

# we can only do 'edge' for now because the 'stable' channel might require stable grade
snapcraft push *.snap --release=edge
