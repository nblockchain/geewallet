#!/usr/bin/env bash
set -euo pipefail

if ! which git >/dev/null 2>&1; then
    echo "checking for git... not found" $'\n'

    echo "$0" $'failed, please install "git" (to populate submodules) first'
    exit 1
fi
echo "Populating git submodules..."

git submodule foreach git fetch --all && git submodule sync --recursive && git submodule update --init --recursive
