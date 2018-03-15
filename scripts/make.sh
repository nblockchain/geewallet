#!/usr/bin/env bash
set -e

./scripts/fsicheck.sh make
./scripts/make.fsx "$@"
