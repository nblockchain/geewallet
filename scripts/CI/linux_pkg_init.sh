#!/usr/bin/env bash
set -euxo pipefail

# only reason to retry here is in case apt without sudo fails
for i in {1..5}; do (apt --yes update || sudo apt --yes update) && break || sleep 60; done

# we don't have retry yet, so we retry&sleep manually...
for i in {1..5}; do (apt install --yes sudo || sudo apt install --yes sudo) && break || sleep 60; done

# we don't have retry yet, so we retry&sleep manually...
for i in {1..5}; do (sudo apt install --yes retry) && break || sleep 60; done
