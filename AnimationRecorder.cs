using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using OpenTK;
using Toolbox;
using Toolbox.Library;
using Toolbox.Library.IO;
using Toolbox.Library.Forms;
using Toolbox.Library.Animations;
using FirstPlugin;

namespace AnimationRecorder
{
    class Program
    {
        static string ReleaseDir;
        static string LibDir;
        static string PluginDir;

        [STAThread]
        static void Main(string[] args)
        {
            // The exe may be in a subfolder of Release; find the Release dir
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            ReleaseDir = FindReleaseDir(exeDir);
            LibDir = Path.Combine(ReleaseDir, "Lib");
            PluginDir = Path.Combine(LibDir, "Plugins");

            Console.WriteLine("[Recorder] Release dir: " + ReleaseDir);

            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            Dictionary<string, string> parsedArgs = ParseArgs(args);

            if (!parsedArgs.ContainsKey("--gfpak"))
            {
                PrintUsage();
                Environment.Exit(1);
            }

            string gfpakPath = parsedArgs["--gfpak"];
            string outputDir = parsedArgs.ContainsKey("--output") ? parsedArgs["--output"] : Path.Combine(ReleaseDir, "recordings");
            int width = parsedArgs.ContainsKey("--width") ? int.Parse(parsedArgs["--width"]) : 1024;
            int height = parsedArgs.ContainsKey("--height") ? int.Parse(parsedArgs["--height"]) : 1024;
            int fps = parsedArgs.ContainsKey("--fps") ? int.Parse(parsedArgs["--fps"]) : 30;
            bool allDirections = parsedArgs.ContainsKey("--all-directions");
            string directionStr = parsedArgs.ContainsKey("--direction") ? parsedArgs["--direction"] : "Front";
            string ffmpegPath = parsedArgs.ContainsKey("--ffmpeg") ? parsedArgs["--ffmpeg"] : "";
            float camOffsetY = parsedArgs.ContainsKey("--cam-offset-y") ? float.Parse(parsedArgs["--cam-offset-y"]) : 1.0f;
            float camOffsetX = parsedArgs.ContainsKey("--cam-offset-x") ? float.Parse(parsedArgs["--cam-offset-x"]) : 0f;
            float camFov = parsedArgs.ContainsKey("--cam-fov") ? float.Parse(parsedArgs["--cam-fov"]) : 0f;
            float camDistance = parsedArgs.ContainsKey("--cam-distance") ? float.Parse(parsedArgs["--cam-distance"]) : 1.0f;
            string animFilter = parsedArgs.ContainsKey("--anim") ? parsedArgs["--anim"] : "";
            float brightness = parsedArgs.ContainsKey("--brightness") ? float.Parse(parsedArgs["--brightness"]) : 1.0f;
            bool trackModel = parsedArgs.ContainsKey("--track");
            bool boneMode = parsedArgs.ContainsKey("--bone");
            bool normalMode = parsedArgs.ContainsKey("--normal");
            bool grayMode = parsedArgs.ContainsKey("--gray");
            bool silhouetteMode = parsedArgs.ContainsKey("--silhouette");
            bool listBones = parsedArgs.ContainsKey("--list-bones");

            if (!File.Exists(gfpakPath))
            {
                Console.Error.WriteLine("[ERROR] GFPAK file not found: " + gfpakPath);
                Environment.Exit(1);
            }

            Console.WriteLine("[Recorder] GFPAK: " + gfpakPath);
            Console.WriteLine("[Recorder] Output: " + outputDir);
            Console.WriteLine("[Recorder] Resolution: " + width + "x" + height + ", FPS: " + fps);

            try
            {
                RunRecording(gfpakPath, outputDir, width, height, fps, allDirections, directionStr, ffmpegPath, camOffsetY, camOffsetX, camFov, camDistance, animFilter, brightness, trackModel, boneMode, normalMode, grayMode, silhouetteMode, listBones, parsedArgs);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[FATAL] " + ex.ToString());
                Environment.Exit(1);
            }
        }

        static void RunRecording(string gfpakPath, string outputDir, int width, int height, int fps,
                                  bool allDirections, string directionStr, string ffmpegPath,
                                  float camOffsetY, float camOffsetX, float camFov, float camDistance,
                                  string animFilter, float brightness, bool trackModel, bool boneMode, bool normalMode, bool grayMode, bool silhouetteMode, bool listBones, Dictionary<string, string> parsedArgs)
        {
            Directory.CreateDirectory(outputDir);

            Runtime.ExecutableDir = ReleaseDir;

            string configPath = Path.Combine(ReleaseDir, "config.xml");
            if (File.Exists(configPath))
            {
                try { Config.StartupFromFile(configPath); }
                catch (Exception ex) { Console.WriteLine("[WARN] Config: " + ex.Message); }
            }

            Console.WriteLine("[Recorder] Creating hidden MainForm...");
            dynamic mainForm = Activator.CreateInstance("Toolbox", "Toolbox.MainForm").Unwrap();
            mainForm.StartPosition = FormStartPosition.Manual;
            mainForm.Location = new System.Drawing.Point(-2000, -2000);
            mainForm.Size = new System.Drawing.Size(1920, 1080);
            mainForm.ShowInTaskbar = false;
            mainForm.Show();
            PumpEvents(2000);

            try
            {
                Console.WriteLine("[Recorder] Loading GFPAK...");
                mainForm.OpenFile(gfpakPath);
                PumpEvents(1500);

                ObjectEditor objectEditor = FindObjectEditor(mainForm);
                if (objectEditor == null)
                {
                    Console.Error.WriteLine("[ERROR] No ObjectEditor found");
                    Environment.Exit(1);
                }
                Console.WriteLine("[Recorder] ObjectEditor: " + objectEditor.Text);

                Console.WriteLine("[Recorder] Scanning tree nodes...");
                List<TreeNode> allNodes = new List<TreeNode>();
                TreeNodeCollection rootNodes = objectEditor.GetNodes();
                Console.WriteLine("[Recorder] Root nodes count: " + rootNodes.Count);

                // First, find and expand the Models and Animations folders to trigger lazy loading
                foreach (TreeNode rootNode in rootNodes)
                {
                    ExpandAllChildren(rootNode);
                }

                CollectAllNodes(rootNodes, allNodes);
                Console.WriteLine("[Recorder] Total nodes after expand: " + allNodes.Count);

                // Find model and animation nodes (they become GFBMDL/GFBANM after folder expansion)
                GFBMDL modelNode = null;
                List<GFBANM> animNodes = new List<GFBANM>();

                foreach (TreeNode node in allNodes)
                {
                    string text = node.Text ?? "";
                    if (node is GFBMDL && modelNode == null)
                    {
                        modelNode = (GFBMDL)node;
                        Console.WriteLine("[Recorder] Model (GFBMDL): " + text);
                    }
                    else if (node is GFBANM && text.EndsWith(".gfbanm"))
                    {
                        animNodes.Add((GFBANM)node);
                    }
                    else if (text.EndsWith(".gfbmdl") && modelNode == null)
                    {
                        // Might still be a wrapper, try to get the underlying format
                        Console.WriteLine("[Recorder] Model candidate (wrapper): " + text + " | " + node.GetType().FullName);
                        if (node.Tag is ArchiveFileInfo)
                        {
                            var info = (ArchiveFileInfo)node.Tag;
                            if (info.FileFormat == null)
                                info.FileFormat = info.OpenFile();
                            if (info.FileFormat is GFBMDL)
                            {
                                modelNode = (GFBMDL)info.FileFormat;
                                Console.WriteLine("[Recorder] Model parsed from ArchiveFileInfo");
                            }
                        }
                    }
                    else if (text.EndsWith(".gfbanm"))
                    {
                        if (node is IAnimationContainer)
                        {
                            animNodes.Add((GFBANM)node);
                        }
                        else if (node.Tag is ArchiveFileInfo)
                        {
                            var info = (ArchiveFileInfo)node.Tag;
                            if (info.FileFormat == null)
                                info.FileFormat = info.OpenFile();
                            if (info.FileFormat is GFBANM)
                            {
                                animNodes.Add((GFBANM)info.FileFormat);
                                Console.WriteLine("[Recorder] Anim parsed: " + text);
                            }
                        }
                    }
                }
                Console.WriteLine("[Recorder] Found model: " + (modelNode != null) + ", animations: " + animNodes.Count);

                if (modelNode == null)
                {
                    Console.Error.WriteLine("[ERROR] No GFBMDL model found");
                    Environment.Exit(1);
                }
                if (animNodes.Count == 0)
                {
                    Console.Error.WriteLine("[ERROR] No animations found");
                    Environment.Exit(1);
                }

                // Activate model FIRST - this creates the ViewportEditor and Viewport
                Console.WriteLine("[Recorder] Activating model (this creates viewport)...");
                ActivateModel(modelNode, objectEditor);
                PumpEvents(1000);

                Viewport viewport = objectEditor.GetViewport();
                if (viewport == null)
                {
                    // Try LibraryGUI as fallback
                    viewport = LibraryGUI.GetActiveViewport();
                }
                if (viewport == null)
                {
                    Console.Error.WriteLine("[ERROR] No viewport found after model activation");
                    Environment.Exit(1);
                }
                Console.WriteLine("[Recorder] Viewport OK, GL_Control=" + (viewport.GL_Control != null));

                // List bones mode
                if (listBones)
                {
                    foreach (var dc in objectEditor.DrawableContainers)
                    {
                        foreach (var d in dc.Drawables)
                        {
                            if (d is STSkeleton)
                            {
                                var skeleton = (STSkeleton)d;
                                Console.WriteLine("[BONES] Total: " + skeleton.bones.Count);
                                for (int i = 0; i < skeleton.bones.Count; i++)
                                {
                                    var b = skeleton.bones[i];
                                    string parentName = b.parentIndex >= 0 ? skeleton.bones[b.parentIndex].Text : "none";
                                    string isEff = b.Text.StartsWith("Eff") ? " [EFF]" : "";
                                    Console.WriteLine("[BONES] " + i + ": " + b.Text + " -> " + parentName + isEff);
                                }
                            }
                        }
                    }
                    return;
                }

                // Resize the ObjectEditor to be large enough
                objectEditor.Size = new System.Drawing.Size(1920, 1080);
                objectEditor.WindowState = FormWindowState.Normal;
                Application.DoEvents();
                PumpEvents(500);

                // Disable grid, bone visualization, axis lines, and orientation cube
                Runtime.displayGrid = false;
                Runtime.renderBones = false;
                Runtime.displayAxisLines = false;
            viewport.GL_Control.ShowOrientationCube = false;

            // Set background to white
            Runtime.backgroundGradientTop = System.Drawing.Color.FromArgb(255, 255, 255);
            Runtime.backgroundGradientBottom = System.Drawing.Color.FromArgb(255, 255, 255);

                // Scale model to fit in frame
                Runtime.previewScale = 0.01f;

                // Reset camera to auto-frame the model
                viewport.GL_Control.ResetCamera(true);

                // Offset camera target to push model down in frame
                float camTargetY = viewport.GL_Control.CameraTarget.Y + 2.0f;
                viewport.GL_Control.CameraTarget = new OpenTK.Vector3(
                    viewport.GL_Control.CameraTarget.X,
                    camTargetY,
                    viewport.GL_Control.CameraTarget.Z
                );

                for (int i = 0; i < 10; i++)
                {
                    viewport.GL_Control.Refresh();
                    Application.DoEvents();
                    Thread.Sleep(100);
                }

                // Read baseline camera angles
                float baseRotY = viewport.GL_Control.CamRotY;
                Console.WriteLine("[Recorder] Base CamRotY=" + baseRotY + " (" + MathHelper.RadiansToDegrees(baseRotY) + " deg) FOV=" + viewport.GL_Control.Fov);

                // Get the actual GL control size for screenshots
                int actualWidth = viewport.GL_Control.Width;
                int actualHeight = viewport.GL_Control.Height;
                Console.WriteLine("[Recorder] GL control actual size: " + actualWidth + "x" + actualHeight);

                // Warm up
                Console.WriteLine("[Recorder] Warming up...");
                for (int i = 0; i < 20; i++)
                {
                    viewport.GL_Control.Refresh();
                    Application.DoEvents();
                    Thread.Sleep(50);
                }

                // --test8: just list animation names
                if (parsedArgs.ContainsKey("--test8"))
                {
                    foreach (var anim in animNodes)
                        Console.WriteLine(anim.Text);
                    return;
                }

                List<int> directions = new List<int>();
                if (allDirections)
                {
                    for (int i = 0; i < 9; i++) directions.Add(i);
                }
                else
                {
                    directions.Add(ParseDirection(directionStr));
                }

                string ffmpeg = FindFFmpeg(ffmpegPath);
                if (string.IsNullOrEmpty(ffmpeg))
                    Console.WriteLine("[WARN] FFmpeg not found, will skip MP4 encoding");
                else
                    Console.WriteLine("[Recorder] FFmpeg: " + ffmpeg);

                for (int animIdx = 0; animIdx < animNodes.Count; animIdx++)
                {
                    GFBANM animNode = animNodes[animIdx];
                    string animName = SanitizeFileName(animNode.Text ?? ("anim_" + animIdx));

                    // --anim filter: only record matching animations
                    if (!string.IsNullOrEmpty(animFilter) && !animName.Contains(animFilter))
                        continue;

                    Console.WriteLine("[Recorder] === Animation " + (animIdx + 1) + "/" + animNodes.Count + ": " + animName + " ===");

                    STAnimation animController = animNode.AnimationController;
                    if (animController == null)
                    {
                        Console.WriteLine("[WARN] No controller, skipping");
                        continue;
                    }

                    int totalFrames = (int)animController.FrameCount;
                    if (totalFrames <= 0) totalFrames = 1;

                    // Try to read actual FPS from animation data
                    int animFps = fps; // default from CLI
                    try
                    {
                        var animConfigField = animNode.GetType().GetField("AnimationData", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (animConfigField != null)
                        {
                            var animData = animConfigField.GetValue(animNode);
                            // AnimationData doesn't store FPS directly, read from FlatBuffer
                        }
                        // Try reading FPS from the FlatBuffer config
                        var loadMethod = animNode.GetType().GetMethod("Load", BindingFlags.Public | BindingFlags.Instance);
                        // FPS is in the FlatBuffer Config struct, but not exposed on GFBANM
                        // Use default FPS for now
                    }
                    catch { }

                    Console.WriteLine("[Recorder] Frames: " + totalFrames + ", FPS: " + animFps);

                    foreach (int dirIdx in directions)
                    {
                        string dirName = GetDirectionName(dirIdx);
                        string animDir = Path.Combine(outputDir, animName, dirName);
                        Directory.CreateDirectory(animDir);

                        Console.WriteLine("[Recorder] Direction: " + dirName);

                        // Reset camera to front view
                        Runtime.previewScale = 0.01f;
                        viewport.GL_Control.ResetCamera(true);

                        // Apply camera offset
                        viewport.GL_Control.CameraTarget = new OpenTK.Vector3(
                            viewport.GL_Control.CameraTarget.X + camOffsetX,
                            viewport.GL_Control.CameraTarget.Y + camOffsetY,
                            viewport.GL_Control.CameraTarget.Z
                        );

                        // Apply FOV override if specified
                        if (camFov > 0)
                            viewport.GL_Control.Fov = camFov;

                        // Apply distance multiplier (smaller FOV = further away)
                        if (camDistance != 1.0f)
                            viewport.GL_Control.Fov = viewport.GL_Control.Fov / camDistance;

                        // Rotate the MODEL for each direction
                        // Rotate the MODEL for each direction
                        float dirAngle = (dirIdx < 8) ? dirIdx * 45f : -30f;  // standard 8 = dirIdx*45, Left30 = -30
                        float dirTilt = (dirIdx < 8) ? 45f : 0f;  // standard 8 = 45deg tilt, Right30 = no tilt
                        RotateModelAxis(viewport, dirAngle, 'Y');
                        if (dirTilt > 0)
                            RotateModelAxis(viewport, dirTilt, 'X');
                        {
                            viewport.GL_Control.Refresh();
                            Application.DoEvents();
                            Thread.Sleep(30);
                        }

                        animController.Reset();
                        animController.SetFrame(0);
                        animController.NextFrame();
                        PumpEvents(200);

                        // Render all frames including frame 0
                        for (int frame = 0; frame < totalFrames; frame++)
                        {
                            animController.SetFrame(frame);
                            animController.NextFrame();

                            // Rotate model after animation update
                            // Rotate model after animation update
                            RotateModelAxis(viewport, dirAngle, 'Y');
                            if (dirTilt > 0)
                                RotateModelAxis(viewport, dirTilt, 'X');
                            // Track model BEFORE render: counteract movement in all axes via ModelTransform
                            if (trackModel)
                            {
                                try
                                {
                                    var editor = LibraryGUI.GetObjectEditor();
                                    if (editor != null)
                                    {
                                        foreach (var dc in editor.DrawableContainers)
                                        {
                                            foreach (var d in dc.Drawables)
                                            {
                                                if (d.GetType().Name == "GFBMDL_Render")
                                                {
                                                    var field = d.GetType().GetField("ModelTransform");
                                                    if (field != null)
                                                    {
                                                        float moveX = 0, moveY = 0, moveZ = 0;
                                                        foreach (var dc2 in editor.DrawableContainers)
                                                        {
                                                            foreach (var d2 in dc2.Drawables)
                                                            {
                                                                if (d2 is Toolbox.Library.STSkeleton)
                                                                {
                                                                    foreach (var bone in ((Toolbox.Library.STSkeleton)d2).bones)
                                                                    {
                                                                        if (bone.Text == "Waist")
                                                                        {
                                                                            moveX = bone.Transform.M41;
                                                                            moveY = bone.Transform.M42;
                                                                            moveZ = bone.Transform.M43;
                                                                            goto foundWaist;
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                        foundWaist:
                                                        float scale = Runtime.previewScale;
                                                        // Counteract movement in all 3 axes
                                                        field.SetValue(d, OpenTK.Matrix4.CreateTranslation(
                                                            -moveX * scale,
                                                            -moveY * scale,
                                                            -moveZ * scale
                                                        ));
                                                    }
                                                    goto doneTrack;
                                                }
                                            }
                                        }
                                    }
                                }
                                catch { }
                                doneTrack:;
                            }

                            // Ensure default shading for regular capture
                            Runtime.viewportShading = Runtime.ViewportShading.Default;
                            Application.DoEvents();
                            Thread.Sleep(50);
                            // Double-refresh to ensure shading takes effect
                            viewport.GL_Control.Refresh();
                            Application.DoEvents();
                            viewport.GL_Control.Refresh();
                            Application.DoEvents();

                            // Capture and save as JPG
                            int captureW = viewport.GL_Control.Width;
                            int captureH = viewport.GL_Control.Height;
                            using (Bitmap bmp = viewport.CreateScreenshot(captureW, captureH, false))
                            {
                                Bitmap toSave = bmp;

                                if (Math.Abs(brightness - 1.0f) > 0.01f)
                                    toSave = AdjustBrightness(bmp, brightness);

                                // Resize to target output size
                                if (toSave.Width != width || toSave.Height != height)
                                {
                                    Bitmap resized = new Bitmap(width, height);
                                    using (Graphics g = Graphics.FromImage(resized))
                                    {
                                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                        g.DrawImage(toSave, 0, 0, width, height);
                                    }
                                    string framePath = Path.Combine(animDir, frame.ToString("D6") + ".jpg");
                                    resized.Save(framePath, ImageFormat.Jpeg);
                                    resized.Dispose();
                                }
                                else
                                {
                                    string framePath = Path.Combine(animDir, frame.ToString("D6") + ".jpg");
                                    toSave.Save(framePath, ImageFormat.Jpeg);
                                }

                                if (toSave != bmp) toSave.Dispose();
                            }

                            // Bone mode: render skeleton-only frame
                            if (boneMode)
                            {
                                RenderBoneFrame(viewport, width, height, animDir, frame);
                            }

                            // Normal mode: render normal map (RGB = normal direction)
                            if (normalMode)
                            {
                                RenderSpecialFrame(viewport, width, height, animDir, frame, "normal", Runtime.ViewportShading.Normal);
                            }

                            // Gray mode: render grayscale with lighting
                            if (grayMode)
                            {
                                RenderGrayFrame(viewport, width, height, animDir, frame, brightness);
                            }

                            // Silhouette mode: render black/white mask
                            if (silhouetteMode)
                            {
                                RenderSilhouetteFrame(viewport, width, height, animDir, frame);
                            }

                            if (frame % 10 == 0 || frame == totalFrames - 1)
                                Console.WriteLine("[Recorder]   Frame " + (frame + 1) + "/" + totalFrames);
                        }

                        // Delete first frame (frame 000000) - often incorrect
                        string firstFrame = Path.Combine(animDir, "000000.jpg");
                        if (File.Exists(firstFrame))
                            File.Delete(firstFrame);

                        // Renumber remaining frames to start from 000000
                        var remainingFrames = Directory.GetFiles(animDir, "*.jpg").OrderBy(f => f).ToArray();
                        for (int i = 0; i < remainingFrames.Length; i++)
                        {
                            string newName = Path.Combine(animDir, i.ToString("D6") + ".jpg");
                            if (remainingFrames[i] != newName)
                                File.Move(remainingFrames[i], newName);
                        }

                        if (!string.IsNullOrEmpty(ffmpeg))
                        {
                            string mp4Path = Path.Combine(outputDir, animName, dirName + ".mp4");
                            EncodeToMp4(ffmpeg, animDir, mp4Path, fps);
                            Console.WriteLine("[Recorder] MP4: " + mp4Path);
                        }
                    }

                    // Create 3x3 composite images for each frame
                    if (allDirections)
                    {
                        Console.WriteLine("[Recorder] Creating composite images...");
                        CreateCompositeImages(outputDir, animName, totalFrames, width, height);

                        // Encode composite video
                        if (!string.IsNullOrEmpty(ffmpeg))
                        {
                            string compositeDir = Path.Combine(outputDir, animName, "composite");
                            string compositeMp4 = Path.Combine(outputDir, animName, "composite.mp4");
                            if (Directory.Exists(compositeDir) && Directory.GetFiles(compositeDir, "*.jpg").Length > 0)
                            {
                                EncodeToMp4(ffmpeg, compositeDir, compositeMp4, fps);
                                Console.WriteLine("[Recorder] Composite MP4: " + compositeMp4);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[FATAL] " + ex.ToString());
            }
            finally
            {
                mainForm.Close();
                mainForm.Dispose();
            }
        }

        static void ActivateModel(GFBMDL modelNode, ObjectEditor editor)
        {
            // Select the node in the tree first
            try
            {
                editor.SelectNode(modelNode);
                Console.WriteLine("[Recorder] Node selected");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[WARN] SelectNode: " + ex.Message);
            }
            PumpEvents(500);

            // Call OnClick to trigger LoadEditor (creates ViewportEditor + Viewport)
            try
            {
                modelNode.OnClick(null);
                Console.WriteLine("[Recorder] OnClick OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[WARN] OnClick: " + ex.Message + "\n" + ex.StackTrace);
            }
            PumpEvents(500);

            // Make sure drawable container is registered
            if (modelNode.DrawableContainer != null)
            {
                Console.WriteLine("[Recorder] DrawableContainer has " + modelNode.DrawableContainer.Drawables.Count + " drawables");

                bool alreadyRegistered = false;
                foreach (var dc in editor.DrawableContainers)
                {
                    if (dc == modelNode.DrawableContainer)
                    {
                        alreadyRegistered = true;
                        break;
                    }
                }

                if (!alreadyRegistered)
                {
                    editor.DrawableContainers.Add(modelNode.DrawableContainer);
                    Console.WriteLine("[Recorder] Registered DrawableContainer");
                }
            }

            if (modelNode.Renderer != null)
            {
                modelNode.Renderer.Visible = true;
            }

            // Now get the viewport and add drawables to it
            Viewport viewport = editor.GetViewport();
            if (viewport == null) viewport = LibraryGUI.GetActiveViewport();
            if (viewport != null && modelNode.DrawableContainer != null)
            {
                foreach (var drawable in modelNode.DrawableContainer.Drawables)
                {
                    drawable.Visible = true;
                    if (!viewport.ContainsDrawable(drawable))
                    {
                        viewport.AddDrawable(drawable);
                        Console.WriteLine("[Recorder] Added drawable: " + drawable.GetType().Name);
                    }
                }
            }
        }

        static void RotateModelRootBone(Viewport viewport, float angleDeg)
        {
            RotateModelAxis(viewport, angleDeg, 'Y');
        }

        static void RotateModelAxis(Viewport viewport, float angleDeg, char axis)
        {
            var editor = LibraryGUI.GetObjectEditor();
            if (editor == null) return;

            OpenTK.Vector3 axisVec;
            if (axis == 'X') axisVec = new OpenTK.Vector3(1, 0, 0);
            else if (axis == 'Y') axisVec = new OpenTK.Vector3(0, 1, 0);
            else axisVec = new OpenTK.Vector3(0, 0, 1);

            foreach (var dc in editor.DrawableContainers)
            {
                foreach (var d in dc.Drawables)
                {
                    if (d is Toolbox.Library.STSkeleton)
                    {
                        var skeleton = (Toolbox.Library.STSkeleton)d;
                        if (skeleton.bones.Count > 0)
                        {
                            foreach (var bone in skeleton.bones)
                            {
                                if (bone.parentIndex == -1)
                                {
                                    Quaternion rot = Quaternion.FromAxisAngle(axisVec, MathHelper.DegreesToRadians(angleDeg));
                                    bone.rot = rot * bone.rot;
                                }
                            }
                            skeleton.update();
                        }
                    }
                }
            }
        }

        static void TrackModel(Viewport viewport, float offsetX, float offsetY)
        {
            // Legacy - not used
        }

        static void TrackModelBones(Viewport viewport, float offsetX, float offsetY)
        {
            // Reset ModelTransform to identity (no translation)
            var editor = LibraryGUI.GetObjectEditor();
            if (editor == null) return;

            foreach (var dc in editor.DrawableContainers)
            {
                foreach (var d in dc.Drawables)
                {
                    if (d.GetType().Name == "GFBMDL_Render")
                    {
                        var field = d.GetType().GetField("ModelTransform");
                        if (field != null)
                        {
                            field.SetValue(d, OpenTK.Matrix4.Identity);
                        }
                    }
                }
            }
        }

        static void FrameCamera(Viewport viewport)
        {
            var containers = viewport.GetActiveContainers();
            if (containers != null && containers.Count > 0)
            {
                Runtime.FrameCamera = true;
                viewport.CenterCamera(viewport.GL_Control, containers);
                Runtime.FrameCamera = false;
                Console.WriteLine("[Recorder] Camera framed");
            }
            else
            {
                Console.WriteLine("[WARN] No containers for framing");
            }
        }

        static ObjectEditor FindObjectEditor(dynamic mainForm)
        {
            try { foreach (Form child in mainForm.MdiChildren) { if (child is ObjectEditor) return (ObjectEditor)child; } } catch { }
            return LibraryGUI.GetObjectEditor();
        }

        static void CollectAllNodes(TreeNodeCollection nodes, List<TreeNode> result)
        {
            foreach (TreeNode node in nodes)
            {
                result.Add(node);
                if (node.Nodes.Count > 0) CollectAllNodes(node.Nodes, result);
            }
        }

        static void ExpandAllChildren(TreeNode node)
        {
            // Trigger OnExpand for nodes that have it (lazy loading)
            try
            {
                if (node is Toolbox.Library.TreeNodeCustom)
                {
                    ((Toolbox.Library.TreeNodeCustom)node).OnExpand();
                }
            }
            catch { }

            foreach (TreeNode child in node.Nodes)
            {
                ExpandAllChildren(child);
            }
        }

        static void PumpEvents(int ms)
        {
            int elapsed = 0;
            while (elapsed < ms)
            {
                Application.DoEvents();
                Thread.Sleep(50);
                elapsed += 50;
            }
        }

        static string FindFFmpeg(string customPath)
        {
            if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
                return customPath;

            // Check next to the exe
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string exePath = Path.Combine(exeDir, "ffmpeg.exe");
            if (File.Exists(exePath)) return exePath;

            // Check in ReleaseDir (next to Toolbox.exe)
            string localPath = Path.Combine(ReleaseDir, "ffmpeg.exe");
            if (File.Exists(localPath)) return localPath;

            // Check in bin/ subfolder
            string binPath = Path.Combine(Path.GetDirectoryName(exeDir), "bin", "ffmpeg.exe");
            if (File.Exists(binPath)) return binPath;

            // Check PATH
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("ffmpeg", "-version");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(3000);
                    if (p.ExitCode == 0) return "ffmpeg";
                }
            }
            catch { }

            return "";
        }

        static void EncodeToMp4(string ffmpeg, string framesDir, string outputPath, int fps)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            string pattern = Path.Combine(framesDir, "%06d.jpg");

            Console.WriteLine("[Recorder] Encoding " + Path.GetFileName(outputPath) + "...");

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = ffmpeg;
            psi.Arguments = "-framerate " + fps + " -i \"" + pattern + "\" -vf \"pad=ceil(iw/2)*2:ceil(ih/2)*2\" -c:v libx264 -pix_fmt yuv420p -y \"" + outputPath + "\"";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;

            try
            {
                using (Process process = Process.Start(psi))
                {
                    bool exited = process.WaitForExit(60000); // 60 second timeout
                    if (!exited)
                    {
                        process.Kill();
                        Console.Error.WriteLine("[ERROR] FFmpeg timed out for " + outputPath);
                    }
                    else if (process.ExitCode != 0)
                    {
                        Console.Error.WriteLine("[ERROR] FFmpeg failed with code " + process.ExitCode);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[ERROR] FFmpeg: " + ex.Message);
            }
        }

        static int ParseDirection(string dir)
        {
            switch (dir.ToLower())
            {
                case "front": return 0;
                case "front-left":
                case "frontleft": return 1;
                case "left": return 2;
                case "back-left":
                case "backleft": return 3;
                case "back": return 4;
                case "back-right":
                case "backright": return 5;
                case "right": return 6;
                case "front-right":
                case "frontright": return 7;
                default: return 0;
            }
        }

        static string GetDirectionName(int idx)
        {
            string[] names = new string[] { "Front_45", "FrontLeft_45", "Left_45", "BackLeft_45", "Back_45", "BackRight_45", "Right_45", "FrontRight_45", "Left30_0" };
            return names[idx];
        }

        static void CreateCompositeImages(string outputDir, string animName, int totalFrames, int width, int height)
        {
            string[] layout = new string[] {
                "BackRight_45", "Back_45", "BackLeft_45",
                "Right_45", "Left30_0", "Left_45",
                "FrontRight_45", "Front_45", "FrontLeft_45"
            };

            string animDir = Path.Combine(outputDir, animName);
            string compositeDir = Path.Combine(animDir, "composite");
            Directory.CreateDirectory(compositeDir);

            int compositeW = width * 3;
            int compositeH = height * 3;
            int frameCount = 0;

            for (int frame = 0; frame < totalFrames; frame++)
            {
                string frameName = frame.ToString("D6") + ".jpg";
                string pngFrameName = frame.ToString("D6") + ".png";

                bool allExist = true;
                foreach (string dir in layout)
                {
                    if (!File.Exists(Path.Combine(animDir, dir, frameName)) &&
                        !File.Exists(Path.Combine(animDir, dir, pngFrameName)))
                    {
                        allExist = false;
                        break;
                    }
                }
                if (!allExist) continue;

                using (Bitmap composite = new Bitmap(compositeW, compositeH))
                {
                    using (Graphics g = Graphics.FromImage(composite))
                    {
                        g.Clear(System.Drawing.Color.Black);
                        for (int i = 0; i < 9; i++)
                        {
                            int col = i % 3;
                            int row = i / 3;
                            string tilePath = Path.Combine(animDir, layout[i], frameName);
                            if (!File.Exists(tilePath))
                                tilePath = Path.Combine(animDir, layout[i], pngFrameName);
                            if (File.Exists(tilePath))
                            {
                                using (Bitmap tile = new Bitmap(tilePath))
                                    g.DrawImage(tile, col * width, row * height, width, height);
                            }
                        }
                    }
                    composite.Save(Path.Combine(compositeDir, frameName), ImageFormat.Jpeg);
                    frameCount++;
                }
            }
            Console.WriteLine("[Recorder] Composite: " + frameCount + " frames");
        }

        static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        static Bitmap AdjustBrightness(Bitmap source, float factor)
        {
            if (Math.Abs(factor - 1.0f) < 0.01f) return source;

            Bitmap result = new Bitmap(source.Width, source.Height);
            using (Graphics g = Graphics.FromImage(result))
            {
                float[][] matrix = {
                    new float[] { factor, 0, 0, 0, 0 },
                    new float[] { 0, factor, 0, 0, 0 },
                    new float[] { 0, 0, factor, 0, 0 },
                    new float[] { 0, 0, 0, 1, 0 },
                    new float[] { 0, 0, 0, 0, 1 }
                };
                var colorMatrix = new System.Drawing.Imaging.ColorMatrix(matrix);
                var attributes = new System.Drawing.Imaging.ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);
                g.DrawImage(source, new System.Drawing.Rectangle(0, 0, source.Width, source.Height),
                    0, 0, source.Width, source.Height, System.Drawing.GraphicsUnit.Pixel, attributes);
            }
            return result;
        }

        static void RenderSpecialFrame(Viewport viewport, int width, int height, string animDir, int frame, string subDir, Runtime.ViewportShading shading)
        {
            try
            {
                Runtime.viewportShading = shading;
                Application.DoEvents();
                Thread.Sleep(30);
                viewport.GL_Control.Refresh();
                Application.DoEvents();
                viewport.GL_Control.Refresh();
                Application.DoEvents();

                int w = viewport.GL_Control.Width;
                int h = viewport.GL_Control.Height;
                using (Bitmap bmp = viewport.CreateScreenshot(w, h, false))
                {
                    // Replace white background with black
                    ReplaceWhiteBackground(bmp);

                    using (Bitmap resized = new Bitmap(width, height))
                    {
                        using (Graphics g = Graphics.FromImage(resized))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(bmp, 0, 0, width, height);
                        }
                        string outDir = Path.Combine(animDir, subDir);
                        Directory.CreateDirectory(outDir);
                        resized.Save(Path.Combine(outDir, frame.ToString("D6") + ".png"), ImageFormat.Png);
                    }
                }

                // Restore to default shading
                Runtime.viewportShading = Runtime.ViewportShading.Default;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[WARN] " + subDir + " render: " + ex.Message);
            }
        }

        static void RenderGrayFrame(Viewport viewport, int width, int height, string animDir, int frame, float brightness)
        {
            try
            {
                var origShading = Runtime.viewportShading;
                Runtime.viewportShading = Runtime.ViewportShading.Default;

                viewport.GL_Control.Refresh();
                Application.DoEvents();
                Thread.Sleep(20);

                int w = viewport.GL_Control.Width;
                int h = viewport.GL_Control.Height;
                using (Bitmap bmp = viewport.CreateScreenshot(w, h, false))
                {
                    // Replace white background with black
                    ReplaceWhiteBackground(bmp);

                    // Convert to white model: each pixel becomes white * its brightness
                    Bitmap whiteModel = ToWhiteModel(bmp);

                    using (Bitmap resized = new Bitmap(width, height))
                    {
                        using (Graphics g = Graphics.FromImage(resized))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(whiteModel, 0, 0, width, height);
                        }
                        string outDir = Path.Combine(animDir, "gray");
                        Directory.CreateDirectory(outDir);
                        resized.Save(Path.Combine(outDir, frame.ToString("D6") + ".png"), ImageFormat.Png);
                    }
                    whiteModel.Dispose();
                }

                Runtime.viewportShading = origShading;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[WARN] Gray render: " + ex.Message);
            }
        }

        static Bitmap ToWhiteModel(Bitmap source)
        {
            Bitmap result = new Bitmap(source.Width, source.Height);
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    var pixel = source.GetPixel(x, y);
                    float brightness = (pixel.R * 0.299f + pixel.G * 0.587f + pixel.B * 0.114f) / 255f;
                    int val = Math.Min(255, Math.Max(0, (int)(brightness * 255)));
                    result.SetPixel(x, y, System.Drawing.Color.FromArgb(val, val, val));
                }
            }
            return result;
        }

        static void RenderSilhouetteFrame(Viewport viewport, int width, int height, string animDir, int frame)
        {
            try
            {
                var origShading = Runtime.viewportShading;
                Runtime.viewportShading = Runtime.ViewportShading.Normal;

                viewport.GL_Control.Refresh();
                Application.DoEvents();
                Thread.Sleep(20);

                int w = viewport.GL_Control.Width;
                int h = viewport.GL_Control.Height;
                using (Bitmap bmp = viewport.CreateScreenshot(w, h, false))
                {
                    // Replace white background with black
                    ReplaceWhiteBackground(bmp);

                    // Convert to silhouette: any non-black pixel becomes white
                    Bitmap sil = new Bitmap(bmp.Width, bmp.Height);
                    for (int y = 0; y < bmp.Height; y++)
                    {
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            var pixel = bmp.GetPixel(x, y);
                            if (pixel.R > 5 || pixel.G > 5 || pixel.B > 5)
                                sil.SetPixel(x, y, System.Drawing.Color.White);
                            else
                                sil.SetPixel(x, y, System.Drawing.Color.Black);
                        }
                    }

                    using (Bitmap resized = new Bitmap(width, height))
                    {
                        using (Graphics g = Graphics.FromImage(resized))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                            g.DrawImage(sil, 0, 0, width, height);
                        }
                        string outDir = Path.Combine(animDir, "silhouette");
                        Directory.CreateDirectory(outDir);
                        resized.Save(Path.Combine(outDir, frame.ToString("D6") + ".png"), ImageFormat.Png);
                    }
                    sil.Dispose();
                }

                Runtime.viewportShading = origShading;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[WARN] Silhouette render: " + ex.Message);
            }
        }

        static void ReplaceWhiteBackground(Bitmap bmp)
        {
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    var pixel = bmp.GetPixel(x, y);
                    if (pixel.R > 240 && pixel.G > 240 && pixel.B > 240)
                    {
                        bmp.SetPixel(x, y, System.Drawing.Color.Black);
                    }
                }
            }
        }

        static Bitmap ToGrayscale(Bitmap source)
        {
            Bitmap result = new Bitmap(source.Width, source.Height);
            using (Graphics g = Graphics.FromImage(result))
            {
                float[][] matrix = {
                    new float[] { 0.299f, 0.299f, 0.299f, 0, 0 },
                    new float[] { 0.587f, 0.587f, 0.587f, 0, 0 },
                    new float[] { 0.114f, 0.114f, 0.114f, 0, 0 },
                    new float[] { 0, 0, 0, 1, 0 },
                    new float[] { 0, 0, 0, 0, 1 }
                };
                var colorMatrix = new System.Drawing.Imaging.ColorMatrix(matrix);
                var attributes = new System.Drawing.Imaging.ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);
                g.DrawImage(source, new System.Drawing.Rectangle(0, 0, source.Width, source.Height),
                    0, 0, source.Width, source.Height, System.Drawing.GraphicsUnit.Pixel, attributes);
            }
            return result;
        }

        static void RenderBoneFrame(Viewport viewport, int width, int height, string animDir, int frame)
        {
            try
            {
                int w = viewport.GL_Control.Width;
                int h = viewport.GL_Control.Height;
                // boneDir should include direction: outputDir/animName/bone/dirName
                string dirName = Path.GetFileName(animDir);
                string animDirParent = Path.GetDirectoryName(animDir);
                string boneDir = Path.Combine(animDirParent, "bone", dirName);
                Directory.CreateDirectory(boneDir);

                // Get skeleton
                var editor = LibraryGUI.GetObjectEditor();
                if (editor == null) return;
                
                STSkeleton skeleton = null;
                foreach (var dc in editor.DrawableContainers)
                {
                    foreach (var d in dc.Drawables)
                    {
                        if (d is STSkeleton) { skeleton = (STSkeleton)d; break; }
                    }
                    if (skeleton != null) break;
                }
                
                if (skeleton == null || skeleton.bones.Count == 0) return;

                // Set black background for bone mode
                var origTop = Runtime.backgroundGradientTop;
                var origBot = Runtime.backgroundGradientBottom;
                Runtime.backgroundGradientTop = System.Drawing.Color.FromArgb(0, 0, 0);
                Runtime.backgroundGradientBottom = System.Drawing.Color.FromArgb(0, 0, 0);

                // Force a render with black background
                viewport.GL_Control.Refresh();
                Application.DoEvents();
                Thread.Sleep(50);

                // Read camera parameters directly from GL control
                var glControl = viewport.GL_Control;
                var glType = glControl.GetType();
                
                Vector3 camTarget = Vector3.Zero;
                float camDist = 10f;
                float camRotX = 0f, camRotY = 0f;
                float fov = 0.5236f;
                
                try {
                    var t = glType.GetField("CameraTarget");
                    if (t != null) camTarget = (Vector3)t.GetValue(glControl);
                    var d = glType.GetField("CameraDistance");
                    if (d != null) camDist = (float)d.GetValue(glControl);
                    var rx = glType.GetProperty("CamRotX");
                    if (rx != null) camRotX = (float)rx.GetValue(glControl);
                    var ry = glType.GetProperty("CamRotY");
                    if (ry != null) camRotY = (float)ry.GetValue(glControl);
                    var f = glType.GetProperty("Fov");
                    if (f != null) fov = (float)f.GetValue(glControl);
                } catch {}

                // Build view matrix: orbit camera
                Matrix4 mv = Matrix4.CreateTranslation(0, 0, -camDist) *
                             Matrix4.CreateRotationX(camRotX) *
                             Matrix4.CreateRotationY(camRotY) *
                             Matrix4.CreateTranslation(-camTarget);

                float aspect = (float)width / height;
                Matrix4 pj = Matrix4.CreatePerspectiveFieldOfView(fov, aspect, 0.01f, 1000f);

                // Get model transform from renderer
                Matrix4 modelMatrix = Matrix4.Identity;
                foreach (var dc in editor.DrawableContainers)
                {
                    foreach (var d in dc.Drawables)
                    {
                        if (d.GetType().Name == "GFBMDL_Render")
                        {
                            var mf = d.GetType().GetField("ModelTransform");
                            if (mf != null) modelMatrix = (Matrix4)mf.GetValue(d);
                            break;
                        }
                    }
                }

                // Get model transform from renderer (includes --track offset)
                Matrix4 rendererModelMatrix = Matrix4.Identity;
                foreach (var dc in editor.DrawableContainers)
                {
                    foreach (var d in dc.Drawables)
                    {
                        if (d.GetType().Name == "GFBMDL_Render")
                        {
                            var mf = d.GetType().GetField("ModelTransform");
                            if (mf != null) rendererModelMatrix = (Matrix4)mf.GetValue(d);
                            break;
                        }
                    }
                }

                // Skeleton model = scale * modelTransform
                Matrix4 skelModel = Matrix4.CreateScale(Runtime.previewScale) * rendererModelMatrix;
                Matrix4 mvp = skelModel * mv * pj;

                // Draw bones (skip Eff/helper bones)
                using (Bitmap bmp = viewport.CreateScreenshot(w, h, false))
                {
                    using (Bitmap resized = new Bitmap(width, height))
                    {
                        using (Graphics g = Graphics.FromImage(resized))
                        {
                            // Clear to black background
                            g.Clear(System.Drawing.Color.Black);
                            
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                            Pen bonePen = new Pen(Color.Yellow, 2);
                            Brush jointBrush = Brushes.Red;

                            int drawnBones = 0;
                            foreach (var bone in skeleton.bones)
                            {
                                if (bone.parentIndex < 0 || bone.parentIndex >= skeleton.bones.Count) continue;
                                
                                // Skip Eff/helper bones, BodySkin, Feelers (vines), and root children
                                if (bone.Text.StartsWith("Eff")) continue;
                                if (bone.Text.Contains("Skin")) continue;
                                if (bone.Text.Contains("Feeler")) continue;
                                if (bone.parentIndex == 0) continue;
                                
                                var parent = skeleton.bones[bone.parentIndex];
                                if (parent.parentIndex == 0) continue;
                                
                                // Also skip if parent name contains Feeler
                                if (parent.Text.Contains("Feeler")) continue;

                                Vector3 bonePos = bone.Transform.ExtractTranslation();
                                Vector3 parentPos = parent.Transform.ExtractTranslation();

                                Vector4 clipBone = new Vector4(bonePos, 1) * mvp;
                                Vector4 clipParent = new Vector4(parentPos, 1) * mvp;

                                if (clipBone.W > 0.01f && clipParent.W > 0.01f)
                                {
                                    float sx = (clipBone.X / clipBone.W + 1) * 0.5f * width;
                                    float sy = (1 - clipBone.Y / clipBone.W) * 0.5f * height;
                                    float px = (clipParent.X / clipParent.W + 1) * 0.5f * width;
                                    float py = (1 - clipParent.Y / clipParent.W) * 0.5f * height;

                                    g.DrawLine(bonePen, px, py, sx, sy);
                                    g.FillEllipse(jointBrush, sx - 3, sy - 3, 6, 6);
                                    drawnBones++;
                                }
                            }

                            bonePen.Dispose();
                        }
                        resized.Save(Path.Combine(boneDir, frame.ToString("D6") + ".png"), ImageFormat.Png);
                    }
                }

                // Restore
                Runtime.renderBones = false;
                Runtime.bonePointSize = 1.0f;
                Runtime.backgroundGradientTop = origTop;
                Runtime.backgroundGradientBottom = origBot;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[WARN] Bone render: " + ex.Message);
            }
        }

        static Dictionary<string, string> ParseArgs(string[] args)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--"))
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    {
                        result[args[i]] = args[i + 1];
                        i++;
                    }
                    else
                    {
                        result[args[i]] = "true";
                    }
                }
            }
            return result;
        }

        static void PrintUsage()
        {
            Console.WriteLine("AnimationRecorder - Record Pokemon GFPAK animations");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  AnimationRecorder.exe --gfpak <path> [options]");
            Console.WriteLine();
            Console.WriteLine("Required:");
            Console.WriteLine("  --gfpak <path>        Path to .gfpak file");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --output <dir>        Output directory (default: ./recordings)");
            Console.WriteLine("  --width <pixels>      Video width (default: 1024)");
            Console.WriteLine("  --height <pixels>     Video height (default: 1024)");
            Console.WriteLine("  --fps <rate>          Video FPS (default: 30)");
            Console.WriteLine("  --direction <name>    Front, FrontLeft, Left, BackLeft, Back, BackRight, Right, FrontRight");
            Console.WriteLine("  --all-directions      Record all 8 directions");
            Console.WriteLine("  --anim <name>         Only record animations matching this name (partial match)");
            Console.WriteLine("  --ffmpeg <path>       Path to ffmpeg.exe");
            Console.WriteLine();
            Console.WriteLine("Camera:");
            Console.WriteLine("  --cam-offset-y <n>    Camera target Y offset (default: 1.0, higher = model lower)");
            Console.WriteLine("  --cam-offset-x <n>    Camera target X offset (default: 0, positive = model moves right)");
            Console.WriteLine("  --cam-fov <n>         Field of view override (default: auto, smaller = zoom out)");
            Console.WriteLine("  --cam-distance <n>    Distance multiplier (default: 1.0, larger = further away)");
            Console.WriteLine("  --brightness <n>      Brightness multiplier (default: 1.0, 1.5=brighter, 0.5=darker)");
            Console.WriteLine("  --track               Enable camera tracking (follow model root bone each frame)");
            Console.WriteLine();
            Console.WriteLine("Debug:");
            Console.WriteLine("  --test8               Test mode: 9 direction screenshots, 1 frame each");
        }

        static string FindReleaseDir(string startDir)
        {
            // Check if Toolbox.exe is in startDir
            if (File.Exists(Path.Combine(startDir, "Toolbox.exe")))
                return startDir;

            // Check parent directories
            DirectoryInfo dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Toolbox.exe")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            // Fallback: try known path
            string knownPath = @"D:\Software\SwitchToolbox\Release";
            if (File.Exists(Path.Combine(knownPath, "Toolbox.exe")))
                return knownPath;

            return startDir;
        }

        static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            string[] searchDirs = new string[] { ReleaseDir, LibDir, PluginDir };
            string[] extensions = new string[] { ".dll", ".exe" };

            foreach (string dir in searchDirs)
            {
                foreach (string ext in extensions)
                {
                    string path = Path.Combine(dir, name + ext);
                    if (File.Exists(path))
                    {
                        try { return Assembly.LoadFrom(path); }
                        catch { }
                    }
                }
            }
            return null;
        }
    }
}

