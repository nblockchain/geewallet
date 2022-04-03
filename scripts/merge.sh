#!/usr/bin/env bash
set -eo pipefail

git fetch origin && \
git merge --no-ff origin/master
