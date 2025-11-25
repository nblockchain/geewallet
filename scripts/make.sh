#!/usr/bin/env bash
set -exo pipefail

FsxRunnerBin=$FsxRunnerBin FsxRunnerArg=$FsxRunnerArg $FsxRunnerBin $FsxRunnerArg ./scripts/make.fsx "$@"
