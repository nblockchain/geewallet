@ECHO OFF

REM please keep this file in sync with configure.bat

SET COMMUNITY="%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Community\Common7\IDE\CommonExtensions\Microsoft\FSharp\fsi.exe"
SET ENTERPRISE="%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Enterprise\Common7\IDE\CommonExtensions\Microsoft\FSharp\fsi.exe"
SET FSXSCRIPT=scripts\make.fsx

IF EXIST %ENTERPRISE% (
    %ENTERPRISE% %FSXSCRIPT% %*
) ELSE (
    IF EXIST %COMMUNITY% (
        %COMMUNITY% %FSXSCRIPT% %*
    ) ELSE (
        ECHO fsi.exe not found, is F# installed?
    )
)
