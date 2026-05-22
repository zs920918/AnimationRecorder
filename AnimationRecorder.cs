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
                RunRecording(gfpakPath, outputDir, width, height, fps, allDirections, directionStr, ffmpegPath, camOffsetY, camOffsetX, camFov, camDistance, animFilter, brightness, trackModel, boneMode, normalMode, grayMode, silhouetteMode, parsedArgs);
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
                                  string animFilter, float brightness, bool trackModel, bool boneMode, bool normalMode, bool grayMode, bool silhouetteMode, Dictionary<string, string> parsedArgs)
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
            MainForm mainForm = new MainForm();
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
                    for (int i = 0; i < 8; i++) directions.Add(i);
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
                        // Y axis = horizontal rotation, X axis = 45° downward tilt
                        RotateModelAxis(viewport, dirIdx * 45f, 'Y');
                        RotateModelAxis(viewport, 45f, 'X');  // 俯角 (looking down)

                        // Warm up
                        for (int i = 0; i < 5; i++)
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
                            RotateModelAxis(viewport, dirIdx * 45f, 'Y');
                            RotateModelAxis(viewport, 45f, 'X');

                            // Track model BEFORE render: counteract X movement via ModelTransform
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
                                                        float moveX = 0;
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
                                                                            goto foundWaist;
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                        foundWaist:
                                                        float scale = Runtime.previewScale;
                                                        field.SetValue(d, OpenTK.Matrix4.CreateTranslation(-moveX * scale, 0, 0));
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
                            Thread.Sleep(10);

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
                                RenderNormalFrame(viewport, width, height, animDir, frame);
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

        static ObjectEditor FindObjectEditor(MainForm mainForm)
        {
            foreach (Form child in mainForm.MdiChildren)
            {
                if (child is ObjectEditor) return (ObjectEditor)child;
            }
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
            string[] names = new string[] { "Front", "FrontLeft", "Left", "BackLeft", "Back", "BackRight", "Right", "FrontRight" };
            return names[idx];
        }

        static float GetDirectionAngle(int idx)
        {
            // CamRotY=0 is Front-Right in the Toolbox orientation cube
            // Front = -45, each step is +45 degrees clockwise
            float[] angles = new float[] { -45, 0, 45, 90, 135, 180, 225, 270 };
            return angles[idx];
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

        static void RenderNormalFrame(Viewport viewport, int width, int height, string animDir, int frame)
        {
            try
            {
                var origShading = Runtime.viewportShading;
                var origTop = Runtime.backgroundGradientTop;
                var origBot = Runtime.backgroundGradientBottom;

                // Black background + Normal shading
                Runtime.viewportShading = Runtime.ViewportShading.Normal;
                Runtime.backgroundGradientTop = System.Drawing.Color.FromArgb(0, 0, 0);
                Runtime.backgroundGradientBottom = System.Drawing.Color.FromArgb(0, 0, 0);

                viewport.GL_Control.Refresh();
                Application.DoEvents();

                int w = viewport.GL_Control.Width;
                int h = viewport.GL_Control.Height;
                using (Bitmap bmp = viewport.CreateScreenshot(w, h, false))
                {
                    using (Bitmap resized = new Bitmap(width, height))
                    {
                        using (Graphics g = Graphics.FromImage(resized))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(bmp, 0, 0, width, height);
                        }
                        string normalDir = Path.Combine(Path.GetDirectoryName(animDir), "normal");
                        Directory.CreateDirectory(normalDir);
                        resized.Save(Path.Combine(normalDir, frame.ToString("D6") + ".png"), ImageFormat.Png);
                    }
                }

                // Restore
                Runtime.viewportShading = origShading;
                Runtime.backgroundGradientTop = origTop;
                Runtime.backgroundGradientBottom = origBot;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[WARN] Normal render: " + ex.Message);
            }
        }

        static void RenderGrayFrame(Viewport viewport, int width, int height, string animDir, int frame, float brightness)
        {
            try
            {
                var origShading = Runtime.viewportShading;
                var origTop = Runtime.backgroundGradientTop;
                var origBot = Runtime.backgroundGradientBottom;

                // Black background + Default shading
                Runtime.viewportShading = Runtime.ViewportShading.Default;
                Runtime.backgroundGradientTop = System.Drawing.Color.FromArgb(0, 0, 0);
                Runtime.backgroundGradientBottom = System.Drawing.Color.FromArgb(0, 0, 0);

                viewport.GL_Control.Refresh();
                Application.DoEvents();

                int w = viewport.GL_Control.Width;
                int h = viewport.GL_Control.Height;
                using (Bitmap bmp = viewport.CreateScreenshot(w, h, false))
                {
                    Bitmap gray = ToGrayscale(bmp);

                    if (Math.Abs(brightness - 1.0f) > 0.01f)
                    {
                        var adjusted = AdjustBrightness(gray, brightness);
                        gray.Dispose();
                        gray = adjusted;
                    }

                    using (Bitmap resized = new Bitmap(width, height))
                    {
                        using (Graphics g = Graphics.FromImage(resized))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(gray, 0, 0, width, height);
                        }
                        string grayDir = Path.Combine(Path.GetDirectoryName(animDir), "gray");
                        Directory.CreateDirectory(grayDir);
                        resized.Save(Path.Combine(grayDir, frame.ToString("D6") + ".png"), ImageFormat.Png);
                    }
                    gray.Dispose();
                }

                // Restore
                Runtime.viewportShading = origShading;
                Runtime.backgroundGradientTop = origTop;
                Runtime.backgroundGradientBottom = origBot;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[WARN] Gray render: " + ex.Message);
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

        static void RenderSilhouetteFrame(Viewport viewport, int width, int height, string animDir, int frame)
        {
            try
            {
                var origShading = Runtime.viewportShading;
                var origTop = Runtime.backgroundGradientTop;
                var origBot = Runtime.backgroundGradientBottom;

                // Black background + Normal shading (model shows colors, background is black)
                Runtime.viewportShading = Runtime.ViewportShading.Normal;
                Runtime.backgroundGradientTop = System.Drawing.Color.FromArgb(0, 0, 0);
                Runtime.backgroundGradientBottom = System.Drawing.Color.FromArgb(0, 0, 0);

                viewport.GL_Control.Refresh();
                Application.DoEvents();

                int w = viewport.GL_Control.Width;
                int h = viewport.GL_Control.Height;
                using (Bitmap bmp = viewport.CreateScreenshot(w, h, false))
                {
                    // Convert to silhouette: any non-black pixel becomes white
                    Bitmap sil = new Bitmap(bmp.Width, bmp.Height);
                    for (int y = 0; y < bmp.Height; y++)
                    {
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            var pixel = bmp.GetPixel(x, y);
                            // If pixel has any color (not pure black), it's part of the model
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
                        string silDir = Path.Combine(Path.GetDirectoryName(animDir), "silhouette");
                        Directory.CreateDirectory(silDir);
                        resized.Save(Path.Combine(silDir, frame.ToString("D6") + ".png"), ImageFormat.Png);
                    }
                    sil.Dispose();
                }

                // Restore
                Runtime.viewportShading = origShading;
                Runtime.backgroundGradientTop = origTop;
                Runtime.backgroundGradientBottom = origBot;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[WARN] Silhouette render: " + ex.Message);
            }
        }

        static void RenderBoneFrame(Viewport viewport, int width, int height, string animDir, int frame)
        {
            try
            {
                var editor = LibraryGUI.GetObjectEditor();
                if (editor == null) return;

                // Hide all mesh renderers, show only skeleton
                foreach (var dc in editor.DrawableContainers)
                {
                    foreach (var d in dc.Drawables)
                    {
                        // Hide mesh renderers
                        if (d.GetType().Name == "GFBMDL_Render")
                            d.Visible = false;

                        // Show skeleton
                        if (d is Toolbox.Library.STSkeleton)
                            ((Toolbox.Library.STSkeleton)d).Visible = true;
                    }
                }

                // Enable bone rendering, disable grid/axis
                Runtime.renderBones = true;
                Runtime.displayGrid = false;
                Runtime.displayAxisLines = false;
                Runtime.backgroundGradientTop = System.Drawing.Color.FromArgb(0, 0, 0);
                Runtime.backgroundGradientBottom = System.Drawing.Color.FromArgb(0, 0, 0);

                // Render
                viewport.GL_Control.Refresh();
                Application.DoEvents();

                // Capture
                int w = viewport.GL_Control.Width;
                int h = viewport.GL_Control.Height;
                using (Bitmap bmp = viewport.CreateScreenshot(w, h, false))
                {
                    using (Bitmap resized = new Bitmap(width, height))
                    {
                        using (Graphics g = Graphics.FromImage(resized))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(bmp, 0, 0, width, height);
                        }
                        string boneDir = Path.Combine(Path.GetDirectoryName(animDir), "bone");
                        Directory.CreateDirectory(boneDir);
                        resized.Save(Path.Combine(boneDir, frame.ToString("D6") + ".png"), ImageFormat.Png);
                    }
                }

                // Restore: show meshes, hide skeleton, reset colors
                foreach (var dc in editor.DrawableContainers)
                {
                    foreach (var d in dc.Drawables)
                    {
                        if (d.GetType().Name == "GFBMDL_Render")
                            d.Visible = true;
                        if (d is Toolbox.Library.STSkeleton)
                            ((Toolbox.Library.STSkeleton)d).Visible = false;
                    }
                }
                Runtime.backgroundGradientTop = System.Drawing.Color.FromArgb(255, 255, 255);
                Runtime.backgroundGradientBottom = System.Drawing.Color.FromArgb(255, 255, 255);
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
