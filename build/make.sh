#!/usr/bin/env bash
set -e

./build/fsicheck.sh make
./build/make.fsx "$@"
