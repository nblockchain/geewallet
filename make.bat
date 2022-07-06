@ECHO OFF

IF NOT EXIST "scripts\fsx\Tools\fsi.bat" (
    echo "ERROR: configure hasn't been run yet, run .\configure.bat first" && EXIT /b 1
)
CALL scripts\fsx\Tools\fsi.bat scripts\make.fsx %*
