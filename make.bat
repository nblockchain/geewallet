@ECHO OFF

IF NOT EXIST "scripts\build.config" (
    echo "ERROR: configure hasn't been run yet, run .\configure.bat first" && EXIT /b 1
)

where /q dotnet
IF ERRORLEVEL 1 (
    CALL scripts\fsx\Tools\fsi.bat scripts\make.fsx %*
) ELSE (
    dotnet fsi scripts\make.fsx %*
)
