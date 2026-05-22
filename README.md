# AnimationRecorder

从 Pokemon 剑盾 GFPAK 文件中批量提取动画，录制为 PNG 图片序列和 MP4 视频。

## 快速开始

```bash
git clone https://github.com/zs920918/AnimationRecorder.git
cd AnimationRecorder
setup.bat    # 下载依赖 + 编译
```

## 基本使用

```bash
# 录制单个文件的所有动画（默认只录 Front 方向）
lib\AnimationRecorder.exe --gfpak "pm0001_00.gfpak" --output "D:\output"

# 录制全部 9 个方向
lib\AnimationRecorder.exe --gfpak "pm0001_00.gfpak" --output "D:\output" --all-directions

# Python 批量处理
python record_animations.py --input-dir "D:\gfpak_files" --output-dir "D:\output"
```

## 查看动画列表

```bash
lib\AnimationRecorder.exe --gfpak "pm0006_00.gfpak" --output "D:\output" --test8
```

输出中带 `ANIM:` 前缀的就是动画名称。

## 过滤指定动画

`--anim` 支持部分匹配：

```bash
# 只录名字含 "walk" 的动画
lib\AnimationRecorder.exe --gfpak "xxx.gfpak" --output "D:\output" --all-directions --anim walk

# 只录名字含 "run" 的动画
lib\AnimationRecorder.exe --gfpak "xxx.gfpak" --output "D:\output" --all-directions --anim run

# 录全部动画（不加 --anim）
lib\AnimationRecorder.exe --gfpak "xxx.gfpak" --output "D:\output" --all-directions
```

## 输出方向

默认只输出 Front（正前方）。可用 `--direction` 指定单个方向，或 `--all-directions` 输出全部 9 个方向。

| 方向 | 水平旋转 | 俯角 |
|------|---------|------|
| Front_45 | 0° | 45° |
| FrontLeft_45 | 45° | 45° |
| Left_45 | 90° | 45° |
| BackLeft_45 | 135° | 45° |
| Back_45 | 180° | 45° |
| BackRight_45 | 225° | 45° |
| Right_45 | 270° | 45° |
| FrontRight_45 | 315° | 45° |
| FrontRight_0 | 315° | 0°（平视） |

```bash
--direction Front        # 正前
--direction FrontLeft    # 左前
--direction Left         # 正左
--direction BackLeft     # 左后
--direction Back         # 正后
--direction BackRight    # 右后
--direction Right        # 正右
--direction FrontRight   # 右前
--all-directions         # 全部 9 个方向
```

## 相机参数

| 参数 | 说明 | 默认值 |
|------|------|--------|
| `--cam-offset-y` | 上下平移（正值=模型下移） | 1.0 |
| `--cam-offset-x` | 左右平移（正值=模型左移） | 0 |
| `--cam-fov` | 视场角（值越大模型越小） | 自动 |
| `--cam-distance` | 距离倍率（值越大模型越大） | 1.0 |

```bash
# 默认参数
AnimationRecorder.exe --gfpak "pm0006_00.gfpak" --output "D:\output" --all-directions

# 拉远相机（距离 x2）
AnimationRecorder.exe --gfpak "pm0006_00.gfpak" --output "D:\output" --all-directions --cam-distance 2.0

# 模型更靠下 + 更远
AnimationRecorder.exe --gfpak "pm0006_00.gfpak" --output "D:\output" --all-directions --cam-offset-y 5.0 --cam-distance 1.5

# 手动指定 FOV（值越小越远）
AnimationRecorder.exe --gfpak "pm0006_00.gfpak" --output "D:\output" --all-directions --cam-fov 0.3
```

## 渲染模式

除了默认的普通渲染，还支持三种特殊渲染模式。每种模式的输出保存在每个方向文件夹的子目录中。

| 参数 | 输出目录 | 说明 | 背景 |
|------|---------|------|------|
| `--normal` | `normal/` | Normal Map（RGB = 法线方向） | 黑底 |
| `--gray` | `gray/` | 白模（无纹理，保留光照阴影） | 黑底 |
| `--silhouette` | `silhouette/` | 黑白遮罩（模型白色，背景黑色） | 黑底 |

可同时使用多种模式：

```bash
# 同时输出普通渲染 + Normal + 灰模 + 遮罩
AnimationRecorder.exe --gfpak "xxx.gfpak" --output "D:\output" --direction Front --normal --gray --silhouette

# 只输出灰模
AnimationRecorder.exe --gfpak "xxx.gfpak" --output "D:\output" --all-directions --gray
```

## 模型跟踪

`--track` 参数启用模型跟踪，适用于走路、跑步等模型会移动的动画。会自动抵消模型的水平移动，保持模型在画面中央。

```bash
# 不跟踪（默认，适合静止动画）
AnimationRecorder.exe --gfpak "xxx.gfpak" --output "D:\output" --all-directions

# 启用跟踪（适合走路/跑步动画）
AnimationRecorder.exe --gfpak "xxx.gfpak" --output "D:\output" --all-directions --track
```

## 其他参数

| 参数 | 说明 | 默认值 |
|------|------|--------|
| `--gfpak` | GFPAK 文件路径 | 必填 |
| `--output` | 输出目录 | `./recordings` |
| `--width` | 输出宽度 | 1024 |
| `--height` | 输出高度 | 1024 |
| `--fps` | 视频帧率 | 30 |
| `--ffmpeg` | FFmpeg 路径 | 自动查找 |
| `--anim` | 过滤动画名（部分匹配） | 全部 |
| `--test8` | 只输出动画列表 | - |
| `--normal` | 输出 Normal Map | - |
| `--gray` | 输出白模灰度图 | - |
| `--silhouette` | 输出黑白遮罩 | - |
| `--brightness` | 亮度调整（1.0=原样，1.5=更亮） | 1.0 |
| `--track` | 启用模型跟踪 | - |

## 完整示例

```bash
lib\AnimationRecorder.exe ^
  --gfpak "D:\Game\pokemon\pm0059_00.gfpak" ^
  --output "D:\output" ^
  --anim walk01 ^
  --direction Right ^
  --cam-offset-y 1 ^
  --cam-offset-x 1 ^
  --cam-fov 0.3 ^
  --track
```

## 输出结构

```
output/
└── pm0006_00/
    └── pm0006_00_ba01_land01.gfbanm/
        ├── Front_45/           ← 普通渲染（JPG）
        │   ├── 000001.jpg
        │   ├── 000002.jpg
        │   └── ...
        ├── Front_45.mp4
        ├── normal/             ← Normal Map（PNG，黑底）
        │   ├── 000001.png
        │   └── ...
        ├── gray/               ← 白模灰度图（PNG，黑底）
        │   ├── 000001.png
        │   └── ...
        ├── silhouette/         ← 黑白遮罩（PNG，黑底）
        │   ├── 000001.png
        │   └── ...
        ├── FrontLeft_45/
        ├── FrontLeft_45.mp4
        └── ...（共 9 个方向）
```

## 依赖

- Windows 10/11
- .NET Framework 4.8（Windows 自带）
- [Switch Toolbox](https://github.com/KillzXGaming/Switch-Toolbox)（setup.bat 自动下载）
- [FFmpeg](https://ffmpeg.org/)（可选，用于 MP4 编码）
- Python 3.7+（批量脚本需要）
