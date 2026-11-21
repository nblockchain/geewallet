#!/usr/bin/env bash
set -euxo pipefail

# to install its deps
apt install -y snapcraft
apt purge -y snapcraft


apt install -y curl jq squashfs-tools

# Grab the core snap from the stable channel and unpack it in the proper place
curl -L $(curl -H 'X-Ubuntu-Series: 16' 'https://api.snapcraft.io/api/v1/snaps/details/core' | jq '.download_url' -r) --output core.snap
mkdir -p /snap/core
unsquashfs -d /snap/core/current core.snap

# Grab the snapcraft snap from the stable channel and unpack it in the proper place
curl -L $(curl -H 'X-Ubuntu-Series: 16' 'https://api.snapcraft.io/api/v1/snaps/details/snapcraft?channel=stable' | jq '.download_url' -r) --output snapcraft.snap
mkdir -p /snap/snapcraft
unsquashfs -d /snap/snapcraft/current snapcraft.snap

# Create a snapcraft runner (TODO: move version detection to the core of snapcraft)
mkdir -p /snap/bin
echo "#!/bin/sh" > /snap/bin/snapcraft
snap_version="$(awk '/^version:/{print $2}' /snap/snapcraft/current/meta/snap.yaml)" && echo "export SNAP_VERSION=\"$snap_version\"" >> /snap/bin/snapcraft
echo 'exec "$SNAP/usr/bin/python3" "$SNAP/bin/snapcraft" "$@"' >> /snap/bin/snapcraft
chmod +x /snap/bin/snapcraft
