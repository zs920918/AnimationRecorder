@echo off
REM Build script for AnimationRecorder
REM Requires .NET Framework 4.x (comes with Windows)

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set FwDir=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
set LibDir=%~dp0lib

if not exist "%CSC%" (
    echo ERROR: csc.exe not found at %CSC%
    echo Please install .NET Framework 4.x
    exit /b 1
)

echo Compiling AnimationRecorder...

"%CSC%" ^
    /nologo ^
    /target:exe ^
    /platform:x64 ^
    /out:"%LibDir%\AnimationRecorder.exe" ^
    /reference:"%FwDir%\System.dll" ^
    /reference:"%FwDir%\System.Core.dll" ^
    /reference:"%FwDir%\System.Drawing.dll" ^
    /reference:"%FwDir%\System.Windows.Forms.dll" ^
    /reference:"%FwDir%\System.IO.Compression.FileSystem.dll" ^
    /reference:"%LibDir%\Lib\OpenTK.dll" ^
    /reference:"%LibDir%\Lib\OpenTK.GLControl.dll" ^
    /reference:"%LibDir%\Gl_EditorFramework.dll" ^
    /reference:"%LibDir%\Toolbox.Library.dll" ^
    /reference:"%LibDir%\Toolbox.exe" ^
    /reference:"%LibDir%\Lib\Plugins\FirstPlugin.Plg.dll" ^
    "%~dp0AnimationRecorder.cs" ^
    "%~dp0GuiMainForm.cs"

if %ERRORLEVEL% == 0 (
    echo.
    echo Build successful!
    echo Output: %LibDir%\AnimationRecorder.exe
) else (
    echo.
    echo Build FAILED!
    exit /b 1
)
