@echo off
setlocal enabledelayedexpansion
REM Setup script - downloads dependencies and builds AnimationRecorder

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
        goto :check_ffmpeg
    )
)

echo Do you already have Switch Toolbox installed?
echo   1. Yes - specify the path to the Release folder
echo   2. No  - download from GitHub (35MB)
echo.
set /p TOOLBOX_CHOICE="Enter 1 or 2: "

if "!TOOLBOX_CHOICE!"=="1" (
    echo.
    echo Enter the path to the folder containing Toolbox.exe
    echo (usually the Release folder or the extracted zip folder)
    echo.
    set /p TOOLBOX_PATH="Path: "
    
    if not exist "!TOOLBOX_PATH!\Toolbox.exe" (
        echo ERROR: Toolbox.exe not found at "!TOOLBOX_PATH!"
        pause
        exit /b 1
    )
    
    echo Copying files...
    mkdir "%LIB_DIR%" 2>nul
    xcopy "!TOOLBOX_PATH!\*" "%LIB_DIR%\" /E /I /Y >nul
    echo Done!
    goto :check_ffmpeg
)

REM Download from GitHub
echo.
echo Downloading Switch Toolbox from GitHub...
set ZIP_PATH=%PROJ_DIR%toolbox.zip
set URL=https://github.com/KillzXGaming/Switch-Toolbox/releases/download/Final/Toolbox-Latest.zip

powershell -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri '%URL%' -OutFile '%ZIP_PATH%'"
if not exist "%ZIP_PATH%" (
    echo ERROR: Download failed.
    echo Please download manually from: https://github.com/KillzXGaming/Switch-Toolbox/releases
    echo Extract the zip to: %LIB_DIR%\
    pause
    exit /b 1
)

echo Extracting...
powershell -Command "Expand-Archive -Path '%ZIP_PATH%' -DestinationPath '%LIB_DIR%' -Force"
del "%ZIP_PATH%"

REM The zip extracts to a subfolder. Move contents up if needed.
for /d %%d in ("%LIB_DIR%\*") do (
    if exist "%%d\Toolbox.exe" (
        echo Moving files from %%d to %LIB_DIR%...
        xcopy "%%d\*" "%LIB_DIR%\" /E /I /Y >nul
        rmdir /s /q "%%d"
    )
)

echo Toolbox installed!

:check_ffmpeg
echo.
echo [2/3] Checking FFmpeg...

set BIN_DIR=%PROJ_DIR%bin
set FFMPEG_PATH=

REM Check common locations
if exist "%BIN_DIR%\ffmpeg.exe" set FFMPEG_PATH=%BIN_DIR%\ffmpeg.exe
if exist "%LIB_DIR%\ffmpeg.exe" set FFMPEG_PATH=%LIB_DIR%\ffmpeg.exe

if defined FFMPEG_PATH (
    echo FFmpeg found at %FFMPEG_PATH%
    goto :build
)

echo FFmpeg not found. You can:
echo   1. Specify path to existing ffmpeg.exe
echo   2. Skip (MP4 encoding will not be available)
echo.
set /p FFMPEG_CHOICE="Enter 1 or 2: "

if "!FFMPEG_CHOICE!"=="1" (
    echo.
    set /p FFMPEG_INPUT="Path to ffmpeg.exe: "
    if exist "!FFMPEG_INPUT!" (
        mkdir "%BIN_DIR%" 2>nul
        copy "!FFMPEG_INPUT!" "%BIN_DIR%\ffmpeg.exe" >nul
        echo Copied to %BIN_DIR%\ffmpeg.exe
    ) else (
        echo File not found. Skipping.
    )
)

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
echo   lib\AnimationRecorder.exe --gfpak "path\to\file.gfpak" --output "D:\output" --all-directions
echo.
echo Python batch:
echo   python record_animations.py --input-dir "D:\pokemon\gfpak_files" --output-dir "D:\output"
echo.
pause
