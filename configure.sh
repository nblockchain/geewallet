#!/usr/bin/env bash
set -e

./fsicheck.sh configure
./configure.fsx "$@"
