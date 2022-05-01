@ECHO OFF

IF NOT EXIST "scripts\fsx\Tools\fsi.bat" (
    git submodule sync && git submodule update --init
)
CALL scripts\fsx\Tools\fsi.bat scripts\configure.fsx %*
