#!/usr/bin/env bash
set -eo pipefail

if [ "$#" -gt "1" ]; then
    echo "Not more than one argument can be supplied."
    exit 1
fi

git push origin master
git push gnome master
if [ "$#" -gt "0" ]; then
    git push origin $1
    git push gnome $1
fi
