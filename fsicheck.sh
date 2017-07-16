#!/usr/bin/env bash
set -e

REPL_CHECK_MSG="checking for F# REPL..."
which fsharpi >/dev/null || \
    (echo "$REPL_CHECK_MSG" $'not found\n' && echo "$1" $'failed, please install "fsharpi" first' && exit 1)
if [ "$1" = "configure" ]; then
    echo "$REPL_CHECK_MSG" $'found'
fi

