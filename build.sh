#!/usr/bin/env bash
set -e

nuget restore
xbuild gwallet.sln
