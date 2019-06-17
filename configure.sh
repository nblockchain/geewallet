#!/usr/bin/env bash
set -e

REPL_CHECK_MSG="checking for a working F# REPL..."
FSX_CHECK_MSG="checking for fsx..."

if ! which fsharpi >/dev/null 2>&1; then
    echo "$REPL_CHECK_MSG" $'not found'

    if ! which fsx >/dev/null 2>&1; then
        echo "$FSX_CHECK_MSG" $'not found\n'

        echo "$1" $'failed, please install "fsharpi" or "fsx" first'
        exit 1
    else
        echo "$FSX_CHECK_MSG" $'found'
        RUNNER=fsx
    fi
else
    if ! fsharpi scripts/problem.fsx >/dev/null 2>&1; then
        echo "$REPL_CHECK_MSG" $'not found'

        if ! which fsx >/dev/null 2>&1; then
            echo "$FSX_CHECK_MSG" $'not found\n'

            echo "$1" $'failed, please install "fsx" first'
            exit 1
        else
            echo "$FSX_CHECK_MSG" $'found'
            RUNNER=fsx
        fi
    else
        echo "$REPL_CHECK_MSG" $'found'
        RUNNER=fsharpi
    fi
fi

echo "FsxRunner=$RUNNER" > scripts/build.config

$RUNNER ./scripts/configure.fsx "$@"
