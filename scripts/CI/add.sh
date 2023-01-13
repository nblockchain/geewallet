#!/usr/bin/env bash
set -exo pipefail

echo deb mirror://mirrors.ubuntu.com/mirrors.txt `lsb_release --short --codename`$1 main restricted universe multiverse >> mirrors.txt

