#!/usr/bin/env bash
set -euo pipefail

REPL_CHECK_MSG="checking for a working F# REPL..."

if [ ! -f scripts/fsx/configure.sh ]; then
    if ! which git >/dev/null 2>&1; then
        echo "checking for git... not found" $'\n'

        echo "$0" $'failed, please install "git" (to populate submodule) first'
        exit 1
    fi
    echo "Populating sub-fsx module..."
    git submodule sync --recursive && git submodule update --init --recursive
fi

FSX_CHECK_MSG="checking for fsx..."

if ! which fsharpi >/dev/null 2>&1; then
    echo "$REPL_CHECK_MSG" $'not found'

    if ! which fsx >/dev/null 2>&1; then
        echo "$FSX_CHECK_MSG" $'not found\n'

        echo "$0" $'failed, please install "fsharpi" or "fsx" first'
        exit 1
    else
        echo "$FSX_CHECK_MSG" $'found'
        RUNNER=fsx
    fi
else
    if ! fsharpi scripts/problem.fsx >/dev/null 2>&1; then
        echo "$REPL_CHECK_MSG" $'not found'

        if ! which fsx >/dev/null 2>&1; then
            echo "$FSX_CHECK_MSG" $'not found'

            # fsharpi is broken in Ubuntu 19.04/19.10/20.04 ( https://github.com/fsharp/fsharp/issues/740 )
            BIN_DIR="`pwd`/bin/fsx"
            mkdir -p $BIN_DIR
            cd scripts/fsx && ./configure.sh --prefix=$BIN_DIR && make install && cd ../..
            RUNNER="$BIN_DIR/bin/fsx"
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
