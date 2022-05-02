@ECHO OFF

IF NOT EXIST "scripts\fsx\Tools\fsi.bat" (
    git submodule sync --recursive && git submodule update --init --recursive
)
CALL scripts\fsx\Tools\fsi.bat scripts\configure.fsx %*
