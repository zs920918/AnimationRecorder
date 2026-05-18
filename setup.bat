@echo off
setlocal enabledelayedexpansion
REM AnimationRecorder Setup Script
REM Downloads Switch Toolbox from GitHub and builds the recorder

echo ============================================
echo   AnimationRecorder Setup
echo ============================================
echo.

set PROJ_DIR=%~dp0
set LIB_DIR=%PROJ_DIR%lib

REM Step 1: Toolbox
echo [1/3] Setting up Switch Toolbox...
echo.

if exist "%LIB_DIR%\Toolbox.exe" (
    if exist "%LIB_DIR%\Toolbox.Library.dll" (
        echo Toolbox already configured.
        goto :fix_dlls
    )
)

echo How to get Switch Toolbox:
echo   1. I have it - specify the path
echo   2. Download from GitHub (35MB)
echo.
set /p TOOLBOX_CHOICE="Enter 1 or 2: "

if "!TOOLBOX_CHOICE!"=="1" (
    echo.
    echo Enter path to the folder containing Toolbox.exe
    set /p TOOLBOX_PATH="Path: "
    if not exist "!TOOLBOX_PATH!\Toolbox.exe" (
        echo ERROR: Toolbox.exe not found
        pause
        exit /b 1
    )
    echo Copying...
    mkdir "%LIB_DIR%" 2>nul
    xcopy "!TOOLBOX_PATH!\*" "%LIB_DIR%\" /E /I /Y >nul
    goto :fix_dlls
)

REM Download
echo.
echo Downloading from GitHub...
set ZIP=%PROJ_DIR%toolbox.zip
powershell -Command "[Net.ServicePointManager]::SecurityProtocol='Tls12'; Invoke-WebRequest -Uri 'https://github.com/KillzXGaming/Switch-Toolbox/releases/download/Final/Toolbox-Latest.zip' -OutFile '%ZIP%'"
if not exist "%ZIP%" (
    echo Download failed. Download manually from:
    echo https://github.com/KillzXGaming/Switch-Toolbox/releases
    echo Extract to: %LIB_DIR%\
    pause
    exit /b 1
)
echo Extracting...
mkdir "%LIB_DIR%" 2>nul
powershell -Command "Expand-Archive -Path '%ZIP%' -DestinationPath '%LIB_DIR%' -Force"
del "%ZIP%"

REM Handle nested folder from zip
for /d %%d in ("%LIB_DIR%\*") do (
    if exist "%%d\Toolbox.exe" (
        xcopy "%%d\*" "%LIB_DIR%\" /E /I /Y >nul
        rmdir /s /q "%%d"
    )
)

:fix_dlls
echo.
echo [1.5/3] Fixing DLL conflicts...

REM CRITICAL: Remove duplicate/stub FirstPlugin.Plg.dll files
REM The correct one is in Lib\Plugins\, wrong ones in root and Lib\
if exist "%LIB_DIR%\FirstPlugin.Plg.dll" (
    del "%LIB_DIR%\FirstPlugin.Plg.dll"
    echo Removed wrong FirstPlugin.Plg.dll from root
)
if exist "%LIB_DIR%\FirstPlugin.Plg.pdb" (
    del "%LIB_DIR%\FirstPlugin.Plg.pdb"
)
if exist "%LIB_DIR%\Lib\FirstPlugin.Plg.dll" (
    del "%LIB_DIR%\Lib\FirstPlugin.Plg.dll"
    echo Removed stub FirstPlugin.Plg.dll from Lib\
)

REM Verify correct DLL exists
if not exist "%LIB_DIR%\Lib\Plugins\FirstPlugin.Plg.dll" (
    echo ERROR: Correct FirstPlugin.Plg.dll not found in Lib\Plugins\
    echo Toolbox installation may be incomplete.
    pause
    exit /b 1
)
echo DLL fix complete.

:check_ffmpeg
echo.
echo [2/3] Checking FFmpeg...

if exist "%PROJ_DIR%bin\ffmpeg.exe" (
    echo FFmpeg found.
    goto :build
)

echo FFmpeg not found. MP4 encoding will not be available.
echo Download ffmpeg.exe and place it in: %PROJ_DIR%bin\

:build
echo.
echo [3/3] Building AnimationRecorder...
call "%PROJ_DIR%build.bat"

echo.
echo ============================================
echo   Setup complete!
echo ============================================
echo.
echo Usage:
echo   lib\AnimationRecorder.exe --gfpak "file.gfpak" --output "D:\output" --all-directions
echo.
echo Python batch:
echo   python record_animations.py --input-dir "D:\gfpak_files" --output-dir "D:\output"
echo.
pause
