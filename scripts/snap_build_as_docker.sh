#!/usr/bin/env bash
set -euxo pipefail

# Update system
apt update -y

# Install dependencies
apt install lsb-release docker.io -y

# Install snap and snapcraft
./scripts/snap_install_as_docker.sh

# Build repo from source inside snappy container
docker exec snappy ./configure.sh --prefix=./staging
docker exec snappy make
docker exec snappy make install

# Install snapcraft and dependencies
docker exec snappy snap version
docker exec snappy snap install core20
docker exec snappy snap install --classic --stable snapcraft
docker exec snappy snapcraft --version

# Build snap package
docker exec snappy snapcraft --destructive-mode

# Copy built files from container to host to get the .snap package
#
# Make sure to keep /. at the end of the source directory
# This way docker will copy the directory contents
# instead of the entire directory into the destination directoy.
#
# This method has to be used because `docker cp` does not support
# wildcards (*) in directory paths.
# The name of the .snap package depends on the version so it changes
# and cannot be hardcoded. 
docker cp snappy:/geewallet/. $(pwd)
