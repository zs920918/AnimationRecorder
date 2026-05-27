# AGENTS.md - AnimationRecorder

## What this is

A CLI tool that records Pokemon Sword/Shield GFPAK animations as PNG/JPG image sequences and MP4 videos. Uses Switch Toolbox's OpenGL rendering pipeline internally.

## Build

```bat
.\build.bat
```

- Uses `csc.exe` from .NET Framework 4.8 (not dotnet CLI)
- Output goes to `lib\AnimationRecorder.exe` (NOT `bin/`)
- Source files: `AnimationRecorder.cs` + `GuiMainForm.cs`
- The exe must be in the same directory as `Toolbox.exe` and DLLs to run

## Setup on new machine

```bat
setup.bat
```

Downloads Switch Toolbox from GitHub (35MB), fixes DLL conflicts, builds. Or user can point to existing Toolbox install.

**Critical DLL fix**: Toolbox zip contains two `FirstPlugin.Plg.dll`:
- Root directory (wrong version) — must delete
- `Lib\Plugins\` (correct) — must keep
- `Lib\` (329KB stub) — must delete

setup.bat handles this automatically.

## Architecture

Single-file C# program (`AnimationRecorder.cs`, ~1300 lines) that:
1. Creates a hidden WinForms `MainForm` (for OpenGL context)
2. Loads GFPAK via Toolbox's `STFileLoader.OpenFileFormat()`
3. Navigates the ObjectEditor tree to find `GFBMDL` (model) and `GFBANM` (animation) nodes
4. Activates model via `GFBMDL.OnClick(null)` — this creates ViewportEditor and GL viewport
5. Renders frames via `viewport.GL_Control.Refresh()` + `viewport.CreateScreenshot()`
6. Encodes to MP4 via FFmpeg subprocess

**Key Toolbox API facts:**
- `Runtime.ExecutableDir` is a FIELD, not a property — use `GetField()` not `GetProperty()`
- `Runtime.previewScale` (float) controls model size, default 0.01
- `Runtime.displayGrid`, `Runtime.renderBones`, `Runtime.displayAxisLines` — booleans to hide UI elements
- `Runtime.backgroundGradientTop/Bottom` — background colors (but don't affect rendered output reliably)
- `viewport.GL_Control.CamRotX/CamRotY` — camera rotation in radians (CamRotY controls horizontal orbit)
- `viewport.GL_Control.CameraTarget` — look-at point
- `viewport.GL_Control.ResetCamera(true)` — auto-frame model
- `viewport.CreateScreenshot(w, h, false)` — GL.ReadPixels capture
- `STAnimation.SetFrame(float)` / `NextFrame()` — apply animation at frame
- Model rotation: `RotateModelAxis(viewport, angle, 'Y')` rotates model around Y axis

**Critical: do NOT call `modelNode.OnClick(null)` followed by `skeleton.update()` on bone modifications — this causes 0-frame rendering. The OnClick creates a ViewportEditor which initializes the GL pipeline. After that, bone modifications via `skeleton.update()` break the rendering.**

## 9 Camera directions

Model rotation (not camera rotation) controls direction. Y-axis = horizontal, X-axis = tilt.

| Index | Name | Y rotation | X tilt |
|-------|------|-----------|--------|
| 0 | Front_45 | 0° | 45° |
| 1 | FrontLeft_45 | 45° | 45° |
| 2 | Left_45 | 90° | 45° |
| 3 | BackLeft_45 | 135° | 45° |
| 4 | Back_45 | 180° | 45° |
| 5 | BackRight_45 | 225° | 45° |
| 6 | Right_45 | 270° | 45° |
| 7 | FrontRight_45 | 315° | 45° |
| 8 | Left30_0 | -30° | 0° |

Formula: `dirAngle = (dirIdx < 8) ? dirIdx * 45f : -30f`

## Special rendering modes

Each mode saves to a subdirectory within the direction folder:
- `--normal` → `normal/` — Normal map (RGB = surface normal), black background via post-processing
- `--gray` → `gray/` — White model with lighting (no texture), black background
- `--silhouette` → `silhouette/` — Black/white mask, black background

Background color fix: `Runtime.backgroundGradientTop/Bottom` doesn't reliably change rendered background. Use `ReplaceWhiteBackground()` post-processing to replace white pixels with black.

## Tracking (--track)

Reads the "Waist" bone's Transform matrix after animation update, then applies counter-translation via `GFBMDL_Render.ModelTransform` field (reflection). Tracks all 3 axes (X, Y, Z). Must be applied BEFORE `viewport.GL_Control.Refresh()`.

## Frame handling

- Frame 0 is rendered but deleted afterward (often has artifacts)
- Remaining frames are renumbered to start from 000000
- Output format: JPG for regular renders, PNG for special renders

## Composite images

When `--all-directions` is used, creates 3x3 composite images per frame:

```
BackRight | Back      | BackLeft
Right     | Left30_0  | Left
FrontRight| Front     | FrontLeft
```

Also encodes composite MP4.

## Gotchas

- The `--test8` mode lists animation names without the `.gfbanm` extension in the ANIM: output lines
- Non-gfbanm nodes (hash strings like `718E2B0D8B147F19`) appear in the tree — filter by `.gfbanm` extension
- `Application.DoEvents()` + `Thread.Sleep()` is required between shading mode changes for them to take effect
- GL control size (760x883) is fixed by the Toolbox layout — screenshots are captured at this size then resized
- `Environment.Exit(0)` is needed at the end to force-close the hidden WinForms process
- The `AssemblyResolve` handler must search both `.dll` and `.exe` extensions
- `FindReleaseDir()` walks up parent directories looking for `Toolbox.exe`

## Dependencies

- .NET Framework 4.8 (Windows, csc.exe compiler)
- Switch Toolbox DLLs (GPL-3.0, downloaded by setup.bat)
- FFmpeg (optional, for MP4 encoding, auto-detected or specified via `--ffmpeg`)
- Python 3.7+ (for batch script only)

## Python batch script

```bash
python record_animations.py --input-dir "D:\gfpak_files" --output-dir "D:\output"
```

Calls `AnimationRecorder.exe` for each GFPAK file found.
