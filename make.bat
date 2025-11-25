@ECHO OFF

where /q dotnet
IF ERRORLEVEL 1 (
    CALL scripts\fsx\Tools\fsi.bat scripts\make.fsx %*
) ELSE (
    dotnet fsi scripts\make.fsx %*
)
