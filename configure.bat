@ECHO OFF

IF NOT EXIST "scripts\fsx\Tools\fsi.bat" (
    git submodule sync --recursive && git submodule update --init --recursive
)

where /q dotnet
IF ERRORLEVEL 1 (
    echo FsxRunnerBin=scripts\fsx\Tools\fsi.bat > scripts\build.config
    CALL scripts\fsx\Tools\fsi.bat scripts\configure.fsx %*
) ELSE (
    echo FsxRunnerBin=dotnet > scripts\build.config
    dotnet fsi scripts\configure.fsx %*
)
