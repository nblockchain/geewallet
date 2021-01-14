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
        echo "Automatic login about to begin..."
        echo "$SNAPCRAFT_LOGIN" > snapcraft.login
    else
        echo "No login details found, initiating manual logging-in..."
        snapcraft export-login snapcraft.login
    fi
fi

# if this fails, use `snapcraft export-login` to generate a new token
snapcraft login --with snapcraft.login

echo "Login successfull. Upload starting..."
# the 'stable' and 'candidate' channels require 'stable' grade in the yaml
snapcraft push snap/*.snap --release=edge
