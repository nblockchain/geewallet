#!/usr/bin/env bash
set -e

./build/fsicheck.sh configure
./build/configure.fsx "$@"
