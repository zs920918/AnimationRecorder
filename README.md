# AnimationRecorder

从 Pokemon 剑盾 GFPAK 文件中批量提取动画，录制为 PNG 图片序列和 MP4 视频。

## 快速开始

```bash
git clone https://github.com/zs920918/AnimationRecorder.git
cd AnimationRecorder
setup.bat    # 下载依赖 + 编译
```

## 使用

```bash
# 单个文件，所有动画，9个方向
lib\AnimationRecorder.exe --gfpak "pm0006_00.gfpak" --output "D:\output" --all-directions

# Python 批量
python record_animations.py --input-dir "D:\gfpak_files" --output-dir "D:\output"
```

## 9个拍摄角度

| 方向 | 水平 | 俯角 |
|------|------|------|
| Front_45 ~ FrontRight_45 | 0~315° | 45° |
| FrontRight_0 | 315° | 0° |

## 依赖

- Windows 10/11 + .NET Framework 4.8
- [Switch Toolbox](https://github.com/KillzXGaming/Switch-Toolbox) (setup.bat 自动下载)
- [FFmpeg](https://ffmpeg.org/) (可选，用于 MP4)

## 系统要求

- .NET Framework 4.8（Windows 自带）
- OpenGL 支持的显卡
- Python 3.7+（批量脚本需要）
