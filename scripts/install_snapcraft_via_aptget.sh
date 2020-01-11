#!/usr/bin/env bash
set -euxo pipefail

sudo apt install -y snapcraft

snapcraft --version
