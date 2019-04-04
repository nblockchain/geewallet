#!/usr/bin/env bash
set -e

./scripts/fsicheck.sh configure
./scripts/configure.fsx "$@"
