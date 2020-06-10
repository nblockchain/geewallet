#!/usr/bin/env bash
set -eo pipefail

if [ ! -e snap/*.snap ]; then
    echo "No snap package found."
    exit 1
fi

if [ -e snapcraft.login ]; then
    echo "snapcraft.login found, skipping log-in"
else
    if [[ $SNAPCRAFT_LOGIN ]]; then
        echo "$SNAPCRAFT_LOGIN" > snapcraft.login
    else
        echo "No login details found, initiating manual logging-in..."
        snapcraft export-login snapcraft.login
    fi
fi

snapcraft login --with snapcraft.login

# the 'stable' and 'candidate' channels require 'stable' grade in the yaml
snapcraft push snap/*.snap --release=edge
