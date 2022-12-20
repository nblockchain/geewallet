@ECHO OFF

IF NOT EXIST "scripts\fsx\Tools\fsi.bat" (
    git submodule sync --recursive && git submodule update --init --recursive
)
echo FsxRunnerBin=scripts\fsx\Tools\fsi.bat > scripts\build.config
CALL scripts\fsx\Tools\fsi.bat scripts\configure.fsx %*
