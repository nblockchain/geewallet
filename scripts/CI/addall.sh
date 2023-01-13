#!/usr/bin/env bash
set -euxo pipefail

./add.sh
./add.sh -updates
./add.sh -backports
./add.sh -security
cat /etc/apt/sources.list >> ./mirrors.txt
cp ./mirrors.txt /etc/apt/sources.list || sudo cp ./mirrors.txt /etc/apt/sources.list
