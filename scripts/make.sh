#!/usr/bin/env bash
set -e

source ./scripts/build.config
$FsxRunner ./scripts/make.fsx "$@"
