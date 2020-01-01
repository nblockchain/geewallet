#!/usr/bin/env bash
set -euxo pipefail

sudo apt install -y snapd
snap version
sudo snap install --classic --stable snapcraft

# workaround for GithubActionsCI+snapcraft, see https://forum.snapcraft.io/t/permissions-problem-using-snapcraft-in-azure-pipelines/13258/14?u=knocte
sudo chown root:root /

snapcraft --version
