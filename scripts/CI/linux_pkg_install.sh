#!/usr/bin/env bash
set -euxo pipefail

PREV_DIR=`pwd`
cd "$(dirname "$0")"
# addall.sh is a way to automate this shit: https://askubuntu.com/a/37754/43658
./linux_pkg_init.sh || ./addall.sh
./linux_pkg_init.sh
cd $PREV_DIR

for pkg in "$@"
do
    retry --until=success --times=4 --delay=60 -- sudo DEBIAN_FRONTEND=noninteractive apt install --yes "$pkg"
done
