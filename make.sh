#!/usr/bin/env bash
set -e

./fsicheck.sh make
./make.fsx "$@"
