@echo off
setlocal EnableExtensions

rem =====================================================================
rem  ChokePoint - build script
rem  Put this file in:  .\ChokePointTeams\build.bat
rem  The project lives in:  .\ChokePointTeams\ChokePointTeams\
rem  Each build goes to a new folder:  .\ChokePointTeams\Builds\ChokePointTeams_<date>_<time>\
rem
rem  Usage:
rem    build.bat            full build: includes the .NET runtime (runs on any Windows PC)
rem    build.bat small      smaller build: the PC needs the .NET 8 Desktop Runtime installed
rem    build.bat zip        also creates a .zip next to the build folder
rem    build.bat installer  also creates ChokePointforTeams-Setup-<date>.exe (needs Inno Setup 6 installed)
rem    Options can be combined, e.g.  build.bat small zip installer
rem =====================================================================

set "PROJECT_DIR=%~dp0ChokePointTeams"
set "BUILDS_DIR=%~dp0Builds"
set "APP_NAME=ChokePointTeams"

set "SC=true"
set "ZIP=0"
set "INSTALLER=0"
set "INSTFAIL=0"
for %%A in (%*) do (
    if /I "%%~A"=="small" set "SC=false"
    if /I "%%~A"=="zip" set "ZIP=1"
    if /I "%%~A"=="installer" set "INSTALLER=1"
)

rem ---- checks ----------------------------------------------------------
where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: the "dotnet" command was not found. Install the .NET 8 SDK first.
    goto :fail
)
if not exist "%PROJECT_DIR%\%APP_NAME%.csproj" (
    echo ERROR: could not find "%PROJECT_DIR%\%APP_NAME%.csproj"
    goto :fail
)

rem ---- name of this build: ChokePointTeams_2026-10-15_143205 -----------------
for /f %%I in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd_HHmmss"') do set "STAMP=%%I"
set "OUT=%BUILDS_DIR%\%APP_NAME%_%STAMP%"

echo.
echo Building %APP_NAME%
echo   Project : %PROJECT_DIR%
echo   Output  : %OUT%
if "%SC%"=="true" (echo   Mode    : full, includes the .NET runtime) else (echo   Mode    : small, needs the .NET 8 Desktop Runtime on the PC)
echo.

rem ---- build -----------------------------------------------------------
dotnet publish "%PROJECT_DIR%\%APP_NAME%.csproj" -c Release -r win-x64 --self-contained %SC% -o "%OUT%"
if errorlevel 1 goto :fail

rem ---- make sure the files the app needs are really in the build --------
set "MISSING=0"
for %%F in ("%APP_NAME%.exe" "ui\index.html" "assets\choke-point-splash.png" "WinDivert.dll" "WinDivert64.sys" "THIRD-PARTY-NOTICES.txt" "licenses\WinDivert-LICENSE.txt" "licenses\WinDivert-2.2.2-Source.zip" "licenses\Bangers-OFL.txt" "licenses\TraceEvent-LICENSE.txt" "licenses\WebView2-LICENSE.txt" "licenses\DotNet-LICENSE.txt" "ui\fonts\Bangers-Regular.ttf") do (
    if not exist "%OUT%\%%~F" (
        echo WARNING: missing from the build: %%~F
        set "MISSING=1"
    )
)

rem ---- optional zip ----------------------------------------------------
if "%ZIP%"=="1" (
    echo.
    echo Creating zip...
    powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%OUT%.zip' -Force"
    if errorlevel 1 (echo WARNING: could not create the zip.) else (echo Zip created: %OUT%.zip)
)

rem ---- optional installer (Inno Setup) ----------------------------------
if not "%INSTALLER%"=="1" goto :skipinstaller
call :findiscc
if not defined ISCC goto :noiscc
echo.
echo Creating installer...
"%ISCC%" /Qp "/DBuildDir=%OUT%" "/DStamp=%STAMP%" "%~dp0ChokePointTeams.iss"
if errorlevel 1 goto :isccfailed
echo %BUILDS_DIR%\ChokePointforTeams-Setup-%STAMP%.exe> "%BUILDS_DIR%\last_installer.txt"
echo Installer created in %BUILDS_DIR%
goto :skipinstaller
:isccfailed
echo WARNING: the installer could not be created.
set "INSTFAIL=1"
goto :skipinstaller
:noiscc
echo WARNING: Inno Setup 6 was not found, so no installer was made.
set "INSTFAIL=1"
:skipinstaller

echo.
if "%MISSING%"=="1" (
    echo Build finished, but some files are missing - see the warnings above.
) else (
    echo Build finished OK.
)
echo Folder: %OUT%
if not defined CP_NO_OPEN explorer "%OUT%"
echo.
if not defined CP_NO_PAUSE pause
if "%INSTFAIL%"=="1" exit /b 4
exit /b 0

:fail
echo.
echo BUILD FAILED. See the messages above.
echo.
if not defined CP_NO_PAUSE pause
exit /b 1

:findiscc
rem Looks for Inno Setup's compiler (ISCC.exe) in the usual folders on C: and D:,
rem then in the Windows registry (works for any drive), then on the PATH.
rem You can also set INNO_ISCC to the full path of ISCC.exe to force a location.
set "ISCC="
if defined INNO_ISCC if exist "%INNO_ISCC%" set "ISCC=%INNO_ISCC%"
if defined ISCC goto :eof
for %%P in ("%ProgramFiles(x86)%" "%ProgramFiles%" "D:\Program Files (x86)" "D:\Program Files") do if exist "%%~P\Inno Setup 6\ISCC.exe" set "ISCC=%%~P\Inno Setup 6\ISCC.exe"
if defined ISCC goto :eof
for %%K in ("HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1" "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1" "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1") do for /f "tokens=2,*" %%A in ('reg query %%K /v InstallLocation 2^>nul') do if exist "%%B\ISCC.exe" set "ISCC=%%B\ISCC.exe"
if defined ISCC goto :eof
for /f "delims=" %%I in ('where ISCC.exe 2^>nul') do set "ISCC=%%I"
goto :eof
