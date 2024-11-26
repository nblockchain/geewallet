#!/usr/bin/env bash
set -euo pipefail

RUNNER_BIN=invalid
RUNNER_ARG=invalid

REPL_CHECK_MSG="checking for a working F# REPL..."

if which dotnet >/dev/null 2>&1; then
    echo -n "$REPL_CHECK_MSG"
    RUNNER_BIN=dotnet
    RUNNER_ARG=fsi
else

    cp NuGet-legacy.config NuGet.config

    ./scripts/populate_gitsubmodules.sh

    FSX_CHECK_MSG="checking for fsx..."
    if ! which fsharpi >/dev/null 2>&1; then
        echo "$REPL_CHECK_MSG" $'not found'

        if ! which fsx >/dev/null 2>&1; then
            echo "$FSX_CHECK_MSG" $'not found\n'

            echo "$0" $'failed, please install "dotnet" first'
            exit 1
        else
            echo -n "$FSX_CHECK_MSG"
            RUNNER_BIN=fsx
            RUNNER_ARG=
        fi
    else
        if ! fsharpi scripts/problem.fsx >/dev/null 2>&1; then
            echo "$REPL_CHECK_MSG" $'not found'

            RUNNER_ARG=
            if ! which fsx >/dev/null 2>&1; then
                echo "$FSX_CHECK_MSG" $'not found'

                # fsharpi is broken in Ubuntu 19.04/19.10/20.04 ( https://github.com/fsharp/fsharp/issues/740 )
                BIN_DIR="`pwd`/bin/fsx"
                mkdir -p $BIN_DIR
                cd scripts/fsx && ./configure.sh --prefix=$BIN_DIR && make install && cd ../..
                RUNNER_BIN="$BIN_DIR/bin/fsx"
            else
                echo -n "$FSX_CHECK_MSG"
                RUNNER_BIN=fsx
            fi
        else
            echo -n "$REPL_CHECK_MSG"
            RUNNER_BIN=fsharpi
            RUNNER_ARG="--define:LEGACY_FRAMEWORK"
        fi
    fi

fi

if [ -z "${RUNNER_BIN}" ]; then
    echo "Variable RUNNER_BIN not set. Please report this bug" && exit 1
fi
echo -e "FsxRunnerBin=$RUNNER_BIN" > scripts/build.config
if [ ! -z "${RUNNER_ARG}" ]; then
    echo -e "FsxRunnerArg=$RUNNER_ARG" >> scripts/build.config
fi
source scripts/build.config
DOTNET_NOLOGO=true $RUNNER_BIN $RUNNER_ARG ./scripts/configure.fsx --from-configure "$@"
