#!/usr/bin/env bash
set -exo pipefail

BUILD_CONFIG="./scripts/build.config"
if [ ! -f "$BUILD_CONFIG" ]; then
    echo "ERROR: configure hasn't been run yet, run ./configure.sh first" >&2 && exit 1
fi
source "$BUILD_CONFIG"
FsxRunnerBin=$FsxRunnerBin FsxRunnerArg=$FsxRunnerArg BuildTool=$BuildTool LegacyBuildTool=$LegacyBuildTool $FsxRunnerBin $FsxRunnerArg ./scripts/make.fsx "$@"
