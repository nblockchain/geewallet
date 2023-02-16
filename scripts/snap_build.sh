#!/usr/bin/env bash
set -euxo pipefail

# this is the equivalent of using the 'build-packages' (not stage-packages) section in snapcraft
# but as we're not using the 'make' plugin, we need to this manually now
DEBIAN_FRONTEND=noninteractive sudo apt install -y fsharp build-essential pkg-config cli-common-dev mono-devel


./configure.sh --prefix=./staging
make
if [ `which dotnet` ]
then
    make publish
fi
make install

#this below is to prevent the possible error "Failed to reuse files from previous run: The 'pull' step of 'gwallet' is out of date: The source has changed on disk."
#snapcraft clean gwallet -s pull

snapcraft --destructive-mode
