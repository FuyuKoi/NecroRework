@echo off
setlocal
cd /d "%~dp0"
title Build NecroRework

echo ============================================
echo   Build  NecroRework
echo ============================================
echo.

rem ---- keep dotnet state inside this folder ----
set "DOTNET_CLI_HOME=%~dp0.dotnet_home"
set "NUGET_PACKAGES=%~dp0.nuget_packages"
set "DOTNET_NOLOGO=1"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
if not exist "%DOTNET_CLI_HOME%" mkdir "%DOTNET_CLI_HOME%" 2>nul
if not exist "%NUGET_PACKAGES%" mkdir "%NUGET_PACKAGES%" 2>nul

rem ---- find .NET SDK ----
set "DOTNET="
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
if not defined DOTNET if exist "%USERPROFILE%\Documents\deepseek-harness\default-workspace\tools\dotnet\dotnet.exe" set "DOTNET=%USERPROFILE%\Documents\deepseek-harness\default-workspace\tools\dotnet\dotnet.exe"
if not defined DOTNET if exist "%~dp0tools\dotnet\dotnet.exe" set "DOTNET=%~dp0tools\dotnet\dotnet.exe"

if not defined DOTNET (
    echo [ERROR] .NET SDK not found.
    echo Install it: https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)
echo Compiler: %DOTNET%
echo.

echo [1/3] Compiling...
"%DOTNET%" build "NecroRework\NecroRework.csproj" -c Release
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed. Send the red lines above to the author.
    pause
    exit /b 1
)

echo.
echo [2/3] Assembling package into modpacks\ ...
set "OUT=%~dp0modpacks\NecroRework"
mkdir "%OUT%\localization\zhs" 2>nul

robocopy "NecroRework\bin\Release" "%OUT%" "NecroRework.dll" "NecroRework.json" "NecroRework.pdb" /NFL /NDL /NJH /NJS /NP >nul
robocopy "NecroRework\bin\Release\localization" "%OUT%\localization" /E /NFL /NDL /NJH /NJS /NP >nul

if not exist "%OUT%\NecroRework.dll" (
    echo [ERROR] NecroRework.dll not found after build.
    pause
    exit /b 1
)

echo.
echo [3/3] Packing zip + snapshot...
set "ZIP=%~dp0modpacks\NecroRework.zip"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%OUT%' -DestinationPath '%ZIP%' -Force" >nul

powershell -NoProfile -ExecutionPolicy Bypass -Command "$b='%~dp0'; $d=Join-Path $b ('backups\' + (Get-Date -Format 'yyyy-MM-dd_HHmmss')); New-Item -ItemType Directory -Force $d | Out-Null; robocopy ($b+'NecroRework') ($d+'\NecroRework') /E /XD bin obj lib /NFL /NDL /NJH /NJS /NP | Out-Null; if (Test-Path ($b+'modpacks')) { robocopy ($b+'modpacks') ($d+'\modpacks') /E /NFL /NDL /NJH /NJS /NP | Out-Null }; Get-ChildItem ($b+'backups') -Directory | Sort-Object Name -Descending | Select-Object -Skip 12 | Remove-Item -Recurse -Force" >nul

echo.
echo ============================================
echo   DONE
echo ============================================
echo.
echo Package folder:
echo   %OUT%
echo.
echo Zip:
echo   %ZIP%
echo.
echo Install: copy the NecroRework folder above into
echo   ...\Slay the Spire 2\mods\
echo then restart the game.
echo.
pause
