# 素材、布局及调研来源

本仓库公开独立实现的 C# 程序及其构建材料。游戏运行时代码由本次开发重新编写，使用 C#；以下项目没有作为游戏程序依赖，也没有把它们的游戏脚本复制或翻译进来。

## 原版图像数据

- 数据仓库：[Xkeeper0/smb1](https://github.com/Xkeeper0/smb1)。
- 固定提交：`1cc3c92cbbe8cda44903657f3da332688e806a89`。
- 下载地址：[smb1.chr](https://raw.githubusercontent.com/Xkeeper0/smb1/1cc3c92cbbe8cda44903657f3da332688e806a89/smb1.chr)。
- 本地：[Assets/smb1.chr](Assets/smb1.chr)，8192 字节。
- SHA-256：`6ae7ffd9795d670954b76c19706d4f355b53f49c38a1da14027faee2c9266f10`。

[Art.cs](Scripts/Art.cs) 自行实现 NES 2bpp CHR 的位平面解码、精灵翻转、配色与图像组合。它仅解析声明式精灵配方，把原始图像中的占位像素替换成 CHR 像素。未执行任何参考项目的素材提取代码或游戏代码。图像的原始权利归 Nintendo 等原权利人。

## 地图布局、素材配方与音频

- 参考仓库：[JHDev2006/Super-Mario-Bros.-Remastered-Public](https://github.com/JHDev2006/Super-Mario-Bros.-Remastered-Public)。
- 固定提交：`8d4f3ed7b6c8da7747abed094953144ab3901d1a`。
- 参考项目声明的程序许可证：GPL-3.0；原始许可文本保存在 [Licenses/reference-GPL-3.0.txt](Licenses/reference-GPL-3.0.txt)。该程序许可证不等于其 Nintendo 图像、音乐和其他素材的权利授权。
- 使用的数据范围：SMB1 主线 32 关、奖励区与城堡分段的声明式布局；地形图集的碰撞标记；精灵帧描述、图像重建配方、调色板；PNG、WAV 和 MP3 媒体。
- 本工程数据转换器：[Tools/import_reference_data.py](Tools/import_reference_data.py)。它读取声明式资源文本、解码 TileMap 字节、展开布局继承与父节点位置，输出纯 JSON；没有复制 `.gd`、`.tscn`、`.tres` 游戏实现到交付素材目录。
- 运行时布局：[Data/rooms.json](Data/rooms.json)；图集数据：[Data/atlas.json](Data/atlas.json)。
- 地图主题选择使用 SMB1 经典地上、地下、水下和城堡类别，未启用参考项目的挑战、Boo 竞速或扩展战役。

布局及配方是派生数据，保留来源追踪；其原始关卡设计及媒体权利仍属于原权利人。音频沿用来源库的录音资源，尚未与 NES 卡带输出做逐采样对照。

## 开源项目调研记录

| 项目 | 适合参考的内容 | 本次使用情况 |
| --- | --- | --- |
| [Godot 官方 2D Platformer](https://github.com/godotengine/godot-demo-projects/tree/master/2d/platformer) | Godot 官方的平台游戏示例，MIT | 调研项目；未使用代码 |
| [joshuafolkken/godot-2d-platformer](https://github.com/joshuafolkken/godot-2d-platformer) | Godot 4 平台移动和可变跳跃，MIT | 调研项目；未使用代码 |
| [skarline/smb](https://github.com/skarline/smb) | 较小的马里奥复刻原型 | 未发现明确代码许可证；未使用代码或素材 |
| [SMB Remastered](https://github.com/JHDev2006/Super-Mario-Bros.-Remastered-Public) | 完整主线与附属房间、精灵描述和媒体 | 仅使用上述声明式数据及素材 |
| [Xkeeper0/smb1](https://github.com/Xkeeper0/smb1) | 原始 CHR 图像数据的公开来源 | 使用 8192 字节 CHR 数据 |

不把任何外部项目的代码许可证套用于 Nintendo 原始图像、关卡设计、音乐或商标。本仓库及构建包保留上述来源和权利归属；不将第三方媒体或商标表述为本项目原创，也不把第三方程序许可证解释为 Nintendo 素材的授权。
