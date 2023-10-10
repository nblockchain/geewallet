@ECHO OFF

SET VS_ROOT=%ProgramFiles%\Microsoft Visual Studio\2022
SET VS_ROOT_OLD=%ProgramFiles(x86)%\Microsoft Visual Studio\2019

SET ENTERPRISE="%VS_ROOT%\Enterprise\Common7\IDE\CommonExtensions\Microsoft\FSharp\Tools\fsi.exe"
SET ENTERPRISE_OLD="%VS_ROOT_OLD%\Enterprise\Common7\IDE\CommonExtensions\Microsoft\FSharp\Tools\fsi.exe"
SET ENTERPRISE_OLD_OLD="%VS_ROOT_OLD%\Enterprise\Common7\IDE\CommonExtensions\Microsoft\FSharp\fsi.exe"

SET COMMUNITY="%VS_ROOT%\Community\Common7\IDE\CommonExtensions\Microsoft\FSharp\Tools\fsi.exe"
SET COMMUNITY_OLD="%VS_ROOT_OLD%\Community\Common7\IDE\CommonExtensions\Microsoft\FSharp\Tools\fsi.exe"
SET COMMUNITY_OLD_OLD="%VS_ROOT_OLD%\Community\Common7\IDE\CommonExtensions\Microsoft\FSharp\fsi.exe"

SET BUILDTOOLS="%VS_ROOT%\BuildTools\Common7\IDE\CommonExtensions\Microsoft\FSharp\Tools\fsi.exe"
SET BUILDTOOLS_OLD="%VS_ROOT_OLD%\BuildTools\Common7\IDE\CommonExtensions\Microsoft\FSharp\Tools\fsi.exe"
SET BUILDTOOLS_OLD_OLD="%VS_ROOT_OLD%\BuildTools\Common7\IDE\CommonExtensions\Microsoft\FSharp\fsi.exe"

IF EXIST %ENTERPRISE% (
    SET RUNNER=%ENTERPRISE%
) ELSE (
    IF EXIST %COMMUNITY% (
        SET RUNNER=%COMMUNITY%
    ) ELSE (
        IF EXIST %BUILDTOOLS% (
            SET RUNNER=%BUILDTOOLS%
        ) ELSE (
            IF EXIST %ENTERPRISE_OLD% (
                SET RUNNER=%ENTERPRISE_OLD%
            ) ELSE (
                IF EXIST %COMMUNITY_OLD% (
                    SET RUNNER=%COMMUNITY_OLD%
                ) ELSE (
                    IF EXIST %BUILDTOOLS_OLD% (
                        SET RUNNER=%BUILDTOOLS_OLD%
                    ) ELSE (
                        IF EXIST %ENTERPRISE_OLD_OLD% (
                            SET RUNNER=%ENTERPRISE_OLD_OLD%
                        ) ELSE (
                            IF EXIST %COMMUNITY_OLD_OLD% (
                                SET RUNNER=%COMMUNITY_OLD_OLD%
                            ) ELSE (
                                IF EXIST %BUILDTOOLS_OLD_OLD% (
                                    SET RUNNER=%BUILDTOOLS_OLD_OLD%
                                ) ELSE (
                                    ECHO fsi.exe not found, is F# installed?
                                    EXIT /b 1
                                )
                            )
                        )
                    )
                )
            )
        )
    )
)

%RUNNER% %*
