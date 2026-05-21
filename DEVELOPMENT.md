# AnimationRecorder 开发文档

本文档详细记录了 AnimationRecorder 的开发思路、技术架构和关键实现细节，供后续开发者接手和二次开发。

## 1. 项目背景

### 问题
Pokemon 剑盾（Sword/Shield）的模型和动画存储在 `.gfpak` 文件中。Switch Toolbox 可以打开并预览这些文件，但导出为 DAE/JSON 等格式时有 BUG，只有 Toolbox 预览窗口中的渲染是正确的。

### 目标
利用 Toolbox 自身的渲染管线，逐帧截图并合成视频，绕过导出 BUG。

## 2. Switch Toolbox 架构

### 2.1 目录结构

从 GitHub Release 下载的 Toolbox 解压后结构：

```
Toolbox/
├── Toolbox.exe              # 主程序入口
├── Toolbox.Library.dll      # 核心库（渲染、动画、骨骼系统）
├── GL_EditorFramework.dll   # GL 编辑器框架（摄像机、场景管理）
├── FirstPlugin.Plg.dll      # 文件格式插件（GFPAK、GFBMDL、GFBANM）
├── Toolbox.exe.config       # 程序配置（assembly binding redirects）
├── Lib/
│   ├── OpenTK.dll           # OpenGL 绑定
│   ├── OpenTK.GLControl.dll # WinForms OpenGL 控件
│   ├── Plugins/
│   │   └── FirstPlugin.Plg.dll  # ⚠️ 这个才是正确的插件！
│   └── ...（其他依赖 DLL）
├── Shader/                  # GLSL 着色器
│   ├── GFBModel.vert        # 模型顶点着色器
│   ├── GFBModel.frag        # 模型片段着色器
│   └── ...
├── Hashes/                  # GFPAK 哈希缓存
└── x64/, x86/               # 原生 DLL（DirectXTexNet 等）
```

**关键发现：** GitHub Release 的 Toolbox zip 中有两个 `FirstPlugin.Plg.dll`：
- `FirstPlugin.Plg.dll`（根目录，7.5MB，版本不同）— **不能用**
- `Lib/Plugins/FirstPlugin.Plg.dll`（7.5MB，正确版本）— **必须用这个**

如果加载了错误的插件 DLL，会导致 `GFBMDL.OnClick()` 卡死。`setup.bat` 会自动删除错误的副本。

### 2.2 GFPAK 文件格式

GFPAK 是 Pokemon 剑盾的打包格式，包含：
- **模型** (`.gfbmdl`) — FlatBuffer 格式的 3D 模型
- **动画** (`.gfbanm`) — FlatBuffer 格式的骨骼动画
- **贴图** (`.bntx`) — 纹理文件
- **着色器** (`.bnsh`) — 着色器文件

GFPAK 的树结构：
```
pm0006_00.gfpak
├── bin/pokemon/pm0006_00/
│   ├── mdl/
│   │   └── pm0006_00.gfbmdl
│   └── anm/
│       ├── pm0006_00_ba01_land01.gfbanm
│       ├── pm0006_00_ba02_roar01.gfbanm
│       └── ...
├── Quick access/
│   ├── Models/
│   │   └── pm0006_00.gfbmdl
│   └── Animations/
│       ├── pm0006_00_ba01_land01.gfbanm
│       └── ...
└── Textures/
    └── *.bntx
```

### 2.3 关键类和 API

#### 文件加载
- `STFileLoader.OpenFileFormat(string path)` — 加载任意支持的文件格式
  - 位置：`Toolbox.Library.IO.STFileLoader`
  - 有多个重载，CLI 模式下使用带默认参数的版本

#### 核心类型
- `GFBMDL` — Pokemon 模型（`FirstPlugin` 命名空间）
  - `Renderer` — `GFBMDL_Render` 实例（OpenGL 渲染器）
  - `DrawableContainer` — 包含渲染器和骨骼的容器
  - `Model` — `GFLXModel` 实例（模型数据）
  - `OnClick(TreeView)` — 激活模型（创建 ViewportEditor）
- `GFBANM` — Pokemon 动画
  - `AnimationController` — 返回 `STAnimation` 实例
  - `STAnimation.SetFrame(float)` — 设置当前帧
  - `STAnimation.NextFrame()` — 应用动画到骨骼
  - `STAnimation.FrameCount` — 总帧数
- `GFPAK` — 归档文件
  - 实现 `IArchiveFile` 接口
  - 包含 `ModelFolder`、`AnimationFolder`、`TextureFolder`

#### 渲染系统
- `Viewport` — 3D 视口（`Toolbox.Library.Viewport`）
  - `GL_Control` — `GL_ControlBase` 实例（OpenGL 控件）
  - `CreateScreenshot(int w, int h, bool alpha)` — 截图
  - `AddDrawable(AbstractGlDrawable)` — 添加可绘制对象
  - `GetActiveContainers()` — 获取活跃的 DrawableContainer
- `GFBMDL_Render` — 模型渲染器
  - `ModelTransform` — 模型变换矩阵（可用于跟踪）
  - `Draw()` — 渲染方法
- `STSkeleton` — 骨骼系统
  - `bones` — 骨骼列表
  - `update()` — 更新骨骼矩阵
  - `reset()` — 重置到绑定姿态

#### 摄像机
- `GL_ControlBase`（`GL_EditorFramework.GL_Core`）
  - `CamRotX` / `CamRotY` — 摄像机旋转（弧度）
  - `CameraTarget` — 摄像机目标点
  - `Fov` — 视场角
  - `ResetCamera(bool frameToModel)` — 重置摄像机
  - `ApplyCameraOrientation(int)` — 应用预设视角
  - `Refresh()` — 同步渲染一帧

#### 运行时设置
- `Runtime`（`Toolbox.Library.Runtime`）
  - `ExecutableDir` — **字段**（不是属性！）
  - `MainForm` — 主窗口引用
  - `previewScale` — 模型预览缩放
  - `displayGrid` — 显示网格
  - `renderBones` — 显示骨骼
  - `displayAxisLines` — 显示坐标轴
  - `backgroundGradientTop/Bottom` — 背景颜色

## 3. 核心渲染管线

### 3.1 模型加载流程

```
MainForm.OpenFile(path)
  → STFileLoader.OpenFileFormat(path) → GFPAK
    → ObjectEditor 创建树节点
      → 模型节点 (GFBMDL) 需要用户点击才加载
        → GFBMDL.OnClick() → LoadEditor<STPropertyGrid>()
          → 创建 ViewportEditor
          → 创建 Viewport + GL_Control
          → GFBMDL_Render 注册到场景
```

**关键发现：** 模型的渲染器在 GFPAK 加载时就创建了（`GFBMDL.Renderer`），但需要触发 `OnClick` 才会注册到视口场景中。

### 3.2 动画播放流程

```
GFBANM.AnimationController → STAnimation
  → SetFrame(frame) 设置当前帧
  → NextFrame() 应用动画：
      对每个骨骼组：
        bone.pos = translate值
        bone.rot = rotation值（四元数）
        bone.sca = scale值
      skeleton.update() 更新骨骼矩阵
```

### 3.3 渲染流程

```
GFBMDL_Render.Draw(GL_ControlModern control, Pass pass)
  → UpdateModelMatrix(Scale(previewScale) * ModelTransform)
  → SetBoneUniforms() — 上传骨骼矩阵到 GPU
    → bones[i] = bone.invert * bone.Transform
  → DrawElements(Triangles) — GPU 蒙皮渲染
    → GFBModel.vert: vertex = sum(bones[boneId[i]] * pos * weight[i])
    → GFBModel.frag: PBR 光照计算
```

### 3.4 截图流程

```
Viewport.CreateScreenshot(width, height)
  → GL.BindFramebuffer(0) — 绑定默认帧缓冲
  → GL.ReadPixels(BGRA) — 读取像素
  → BitmapExtension.GetBitmap() — 转换为 Bitmap
  → RotateFlip(RotateNoneFlipY) — 翻转 Y 轴
```

## 4. 开发过程中的关键发现和坑

### 4.1 Assembly 依赖

Toolbox 的 DLL 有复杂的依赖链。运行时需要 `AssemblyResolve` 事件处理程序来加载找不到的程序集：

```csharp
AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
    string name = new AssemblyName(args.Name).Name;
    // 搜索 lib/, lib/Lib/, lib/Lib/Plugins/
    foreach (string dir in searchDirs) {
        string path = Path.Combine(dir, name + ".dll");
        if (File.Exists(path)) return Assembly.LoadFrom(path);
    }
    return null;
};
```

### 4.2 Runtime.ExecutableDir 是字段不是属性

```csharp
// 正确：
Runtime.ExecutableDir = path;  // 字段

// 错误：
// Runtime.ExecutableDir = path;  // 如果用 GetProperty 会返回 null
```

### 4.3 OnClick 卡死问题

`GFBMDL.OnClick()` 调用 `LoadEditor<STPropertyGrid>()` 会创建 `ViewportEditor`，该过程会初始化 GL 控件。如果 GL 上下文未就绪，会卡死。

解决方案：不调用 `OnClick`，直接手动注册渲染器：
```csharp
// 获取 DrawableContainer（在 GFPAK 加载时已创建）
if (modelNode.DrawableContainer != null) {
    foreach (var d in modelNode.DrawableContainer.Drawables)
        d.Visible = true;
}
// 创建 Viewport 手动
Viewport viewport = new Viewport(editor.DrawableContainers);
editor.LoadViewport(viewport);
```

### 4.4 摄像机控制

`CamRotX` 和 `CamRotY` 是弧度制。`ApplyCameraOrientation(3)` 是正前方（CamRotY=0）。

**重要发现：** 直接修改 `CamRotX` 不会影响渲染结果。摄像机系统（InspectCamera）不响应程序化的角度修改。只能通过旋转模型来改变视角。

### 4.5 方向旋转

通过修改根骨骼的四元数旋转来实现模型方向变化：

```csharp
// 水平旋转（Y轴）：控制模型朝向
Quaternion yRot = Quaternion.FromAxisAngle(new Vector3(0,1,0), DegreesToRadians(angle));
bone.rot = yRot * bone.rot;

// 俯角（X轴）：控制俯视角度
Quaternion xRot = Quaternion.FromAxisAngle(new Vector3(1,0,0), DegreesToRadians(45));
bone.rot = xRot * bone.rot;
```

**注意：** 旋转是累积的。如果需要绝对旋转，必须先重置 `bone.rot` 到初始值。

### 4.6 模型跟踪（--track）

对于走路/跑步动画，模型会在场景中移动。通过修改 `GFBMDL_Render.ModelTransform` 来抵消移动：

```csharp
// 找到移动最大的骨骼（通常是 Waist）
float moveX = bone.Transform.M41;

// 设置 ModelTransform 来抵消
var mt = Matrix4.CreateTranslation(-moveX * Runtime.previewScale, 0, 0);
rendererField.SetValue(renderer, mt);
```

**关键：** 必须在渲染之前设置 ModelTransform（在 `Refresh()` 之前），否则不会生效。

### 4.7 FFmpeg 编码

FFmpeg 编码使用 `Process.Start` 调用。注意：
- 不要重定向 stdout/stderr（会导致死锁）
- 图片尺寸必须是偶数（H.264 要求），用 `-vf "pad=ceil(iw/2)*2:ceil(ih/2)*2"` 处理
- 第一帧通常有问题，先渲染再删除

### 4.8 背景颜色

背景使用渐变色，通过 `Runtime.backgroundGradientTop/Bottom` 控制：
```csharp
Runtime.backgroundGradientTop = Color.FromArgb(255, 255, 255);    // 白色
Runtime.backgroundGradientBottom = Color.FromArgb(255, 255, 255); // 白色
```

## 5. 文件结构

```
AnimationRecorder/
├── AnimationRecorder.cs    # 核心程序（CLI + GUI 入口）
├── GuiMainForm.cs          # WinForms GUI（开发中）
├── build.bat               # 编译脚本
├── setup.bat               # 安装脚本（下载 Toolbox + FFmpeg）
├── record_animations.py    # Python 批量处理脚本
├── README.md               # 使用文档
├── DEVELOPMENT.md          # 本开发文档
├── .gitignore
├── bin/                    # 编译输出（AnimationRecorder.exe）
└── lib/                    # Toolbox 依赖（setup.bat 下载）
    ├── Toolbox.exe
    ├── Toolbox.Library.dll
    ├── GL_EditorFramework.dll
    ├── Lib/
    │   ├── OpenTK.dll
    │   └── Plugins/FirstPlugin.Plg.dll
    ├── Shader/
    └── Hashes/
```

## 6. 编译和构建

### 编译命令

```bash
# 使用 .NET Framework 自带的 csc.exe 编译
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe ^
    /target:exe /platform:x64 ^
    /out:lib\AnimationRecorder.exe ^
    /reference:lib\Lib\OpenTK.dll ^
    /reference:lib\Lib\OpenTK.GLControl.dll ^
    /reference:lib\Gl_EditorFramework.dll ^
    /reference:lib\Toolbox.Library.dll ^
    /reference:lib\Toolbox.exe ^
    /reference:lib\Lib\Plugins\FirstPlugin.Plg.dll ^
    AnimationRecorder.cs GuiMainForm.cs
```

### 依赖说明

| DLL | 用途 |
|-----|------|
| `Toolbox.Library.dll` | 核心渲染、动画、骨骼系统 |
| `GL_EditorFramework.dll` | GL 编辑器框架（摄像机、场景） |
| `FirstPlugin.Plg.dll` | Pokemon 文件格式解析 |
| `OpenTK.dll` | OpenGL 绑定 |
| `OpenTK.GLControl.dll` | WinForms GL 控件 |

## 7. 二次开发指南

### 7.1 添加新的拍摄角度

在 `AnimationRecorder.cs` 的 `RunRecording` 方法中修改 `dirCfg` 和 `dirNames` 数组：

```csharp
float[][] dirCfg = new float[][] {
    new float[] { yAngle, xAngle },  // [水平旋转, 俯角]
    ...
};
string[] dirNames = new string[] { "DirectionName", ... };
```

### 7.2 修改模型缩放

修改 `Runtime.previewScale`（默认 0.01）：
```csharp
Runtime.previewScale = 0.005f;  // 更小 = 模型更小
```

### 7.3 支持其他 Pokemon 游戏

如果其他游戏也使用 GFPAK 格式，只需修改 GFPAK 的解析逻辑。如果是其他格式，需要：
1. 实现 `IFileFormat` 接口
2. 实现对应的渲染器
3. 注册到 Toolbox 的插件系统

### 7.4 修改背景颜色

在相机设置代码后添加：
```csharp
Runtime.backgroundGradientTop = System.Drawing.Color.FromArgb(r, g, b);
Runtime.backgroundGradientBottom = System.Drawing.Color.FromArgb(r, g, b);
```

### 7.5 修改跟踪骨骼名

在 `--track` 代码中搜索 `"Waist"` 字符串并替换为目标骨骼名：
```csharp
if (bone.Text == "Waist")  // 改成目标骨骼名
```

## 8. 已知限制

1. **GL 控件大小固定** — 渲染区域由 Toolbox 的 GL 控件决定，不能随意调整
2. **摄像机不能程序化控制** — CamRotX/CamRotY 修改不生效，只能旋转模型
3. **走路动画跟踪不完美** — ModelTransform 偏移有轻微误差
4. **第一帧通常有问题** — 已自动删除并重新编号
5. **某些动画会导致模型出框** — 可通过调整 cam-distance 缓解

## 9. 调试技巧

- 使用 `--test8` 快速查看动画列表和 9 方向预览
- 用 `Console.WriteLine` 输出骨骼 Transform 值来调试跟踪
- FFmpeg 编码失败时检查图片尺寸是否为偶数
- 如果模型不显示，检查 `Runtime.displayGrid/renderBones/displayAxisLines` 是否为 false
- 如果程序卡死，可能是 `OnClick` 调用导致，改用手动注册方式
