r"""
Batch Pokemon Animation Recorder
Records all animations from GFPAK files using AnimationRecorder.exe

Usage:
    python record_animations.py --input "D:\pokemon\pm0006_00.gfpak" --output-dir "D:\pokemon\recordings"
    python record_animations.py --input-dir "D:\pokemon\gfpak_files" --output-dir "D:\pokemon\recordings"
    python record_animations.py --input-list "gfpak_list.txt" --output-dir "D:\pokemon\recordings"
"""

import argparse
import subprocess
import os
import sys
import glob
from pathlib import Path


SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
RECORDER_PATH = os.path.join(SCRIPT_DIR, "AnimationRecorder", "AnimationRecorder.exe")

DIRECTIONS = [
    "Front", "FrontLeft", "Left", "BackLeft",
    "Back", "BackRight", "Right", "FrontRight"
]


def record_single(gfpak_path, output_dir, width=1024, height=1024,
                   fps=30, all_directions=True, direction="Front", ffmpeg_path="",
                   recorder_path=None):
    """Record a single GFPAK file."""
    if recorder_path is None:
        recorder_path = RECORDER_PATH

    print(f"\n{'='*60}")
    print(f"Recording: {os.path.basename(gfpak_path)}")
    print(f"Output:    {output_dir}")
    print(f"{'='*60}")

    cmd = [
        recorder_path,
        "--gfpak", gfpak_path,
        "--output", output_dir,
        "--width", str(width),
        "--height", str(height),
        "--fps", str(fps),
    ]

    if all_directions:
        cmd.append("--all-directions")
    else:
        cmd.extend(["--direction", direction])

    if ffmpeg_path:
        cmd.extend(["--ffmpeg", ffmpeg_path])

    try:
        result = subprocess.run(cmd, timeout=7200)
        if result.returncode != 0:
            print(f"ERROR: Recording failed (exit code {result.returncode})")
            return False
        return True
    except subprocess.TimeoutExpired:
        print(f"ERROR: Recording timed out (2 hour limit)")
        return False
    except Exception as e:
        print(f"ERROR: {e}")
        return False


def main():
    parser = argparse.ArgumentParser(description="Batch Pokemon Animation Recorder")
    parser.add_argument("--input", help="Single GFPAK file to record")
    parser.add_argument("--input-dir", help="Directory containing GFPAK files")
    parser.add_argument("--input-list", help="Text file with one GFPAK path per line")
    parser.add_argument("--output-dir", required=True, help="Output directory for recordings")
    parser.add_argument("--width", type=int, default=1024, help="Video width (default: 1024)")
    parser.add_argument("--height", type=int, default=1024, help="Video height (default: 1024)")
    parser.add_argument("--fps", type=int, default=30, help="Video FPS (default: 30)")
    parser.add_argument("--direction", default="Front",
                        help="Single direction (default: Front). Ignored if --all-directions")
    parser.add_argument("--all-directions", action="store_true", default=True,
                        help="Record all 8 directions (default)")
    parser.add_argument("--single-direction", action="store_true",
                        help="Record only the specified direction")
    parser.add_argument("--ffmpeg", default="",
                        help="Path to ffmpeg.exe")
    parser.add_argument("--recorder", default=RECORDER_PATH,
                        help="Path to AnimationRecorder.exe")

    args = parser.parse_args()

    recorder_path = args.recorder

    if not os.path.exists(recorder_path):
        print(f"ERROR: AnimationRecorder.exe not found at {recorder_path}")
        print("Run build.bat first to compile it.")
        sys.exit(1)

    # Collect GFPAK files
    gfpak_files = []

    if args.input:
        if os.path.exists(args.input):
            gfpak_files.append(os.path.abspath(args.input))
        else:
            print(f"ERROR: File not found: {args.input}")
            sys.exit(1)

    if args.input_dir:
        for ext in ["*.gfpak", "**/*.gfpak"]:
            gfpak_files.extend(glob.glob(os.path.join(args.input_dir, ext), recursive=True))

    if args.input_list:
        with open(args.input_list, "r") as f:
            for line in f:
                line = line.strip()
                if line and not line.startswith("#") and os.path.exists(line):
                    gfpak_files.append(os.path.abspath(line))

    if not gfpak_files:
        print("ERROR: No GFPAK files found")
        sys.exit(1)

    # Remove duplicates
    seen = set()
    unique_files = []
    for f in gfpak_files:
        if f not in seen:
            seen.add(f)
            unique_files.append(f)
    gfpak_files = sorted(unique_files)

    print(f"Found {len(gfpak_files)} GFPAK file(s)")

    all_directions = not args.single_direction

    # Record each file
    success = 0
    failed = 0
    for i, gfpak_path in enumerate(gfpak_files):
        print(f"\n[{i+1}/{len(gfpak_files)}] {os.path.basename(gfpak_path)}")

        gfpak_name = Path(gfpak_path).stem
        file_output_dir = os.path.join(args.output_dir, gfpak_name)

        if record_single(
            gfpak_path, file_output_dir,
            args.width, args.height, args.fps,
            all_directions, args.direction, args.ffmpeg,
            recorder_path
        ):
            success += 1
        else:
            failed += 1

    print(f"\n{'='*60}")
    print(f"DONE: {success} succeeded, {failed} failed out of {len(gfpak_files)} total")
    print(f"{'='*60}")


if __name__ == "__main__":
    main()
