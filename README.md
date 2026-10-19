# Mario C# · Godot / OpenHarmony

使用 Godot .NET 与独立 C# 游戏逻辑实现的 Super Mario Bros. 研究工程，包含 1-1 至 8-4 的 32 个主关及地下、云端、传送区和城堡分段等共 57 个房间。

[v0.1.3 未签名 HAP 下载](https://github.com/kdada/mario-csharp/releases/tag/v0.1.3) · [素材来源](SOURCES.md) · [验证范围](VERIFICATION.md)

开发、构建、核验和 Git 提交统一在当前工程进行，只维护这一份工程。发布构建使用隔离缓存，不修改正在 Editor 中运行的项目或其 NuGet 中间目录。

## 操作

| 操作 | 键盘 | 手柄 |
| --- | --- | --- |
| 移动 | A/D、方向键 | 左摇杆、方向键 |
| 跳跃、游泳 | J | A |
| 冲刺、火球 | K | X；右肩键也可冲刺 |
| 蹲下、进管道 | S、↓ | ↓ |
| 爬藤蔓 | W、↑ | ↑ |
| 确认、暂停 | Enter | Start |
| 返回 | Esc、菜单中的 K | B |
| 静音 | M | — |

支持向左回走、双向跟随相机、32 关选关、本地进度及最高分。K＋长按 J 的跳高约 65 像素；TIME 每秒减少 1.5 单位；蘑菇和花变身时暂停场景 0.72 秒。

## v0.1.3 修复

- 敌人向上碰到玩家脚底时按相对接触判定踩踏；水中可以踩死章鱼和鱼，侧撞和从下顶敌仍受伤。普通存活怪物接触角色后立即面向角色，常规行走和游鱼同步水平速度；踩踏、龟壳和已发射敌方弹道保持各自规则。
- 出管道时统一相机位置，避免 8-4 到达后跳回左侧。陆地水平管道要求落地；水下保留泳姿，在原房间完整播放游入过程。液体背景先于人物和实体地形绘制，避免遮住 8-4 岩石或提前盖住游入角色。
- 水关鱼群从屏外合法水域的上、中、下层轮换生成，持续覆盖后段，限制附近数量并清理离屏生成鱼；预置怪物保留回走行为。食人花初始隐藏，暂停、变身及管道过渡冻结相关时钟；熔岩火泡从液面下等待、跃起并回落。
- 实际跑速超过 100 像素/秒时可以跨过同高地面之间不超过一格的窄坑；普通走速、宽坑、高低落差、空中、水中及移动平台不获得这项支撑。
- 抓旗后脚底停在真实底座顶面，再通过重力和地形碰撞向右走下，避免清关动画直接修改纵坐标穿过砖块。练习模式暂停菜单增加 BIG MARIO / FIRE MARIO，保留位置、速度、生命和时间，低天花板下安全蹲姿恢复。
- 最终公主使用单帧 NPC 定义；飞龟翅膀挂在壳背并随朝向翻转、播放 Flap；Lakitu 存活时云遮住下半身，死亡时本体在云前掉出；弹簧只裁一帧并在真实触碰后压缩、回弹；方块金币下落时在砖块前显示。
- 标题页接入资源库的 [SMB1 标题曲](Assets/Audio/BGM/TitleSMB.mp3)。死亡先播放 [短前奏](Assets/Audio/SFX/DieSting.wav)，0.7 秒后接上 [完整死亡片段](Assets/Audio/BGM/PlayerDie.wav)，并为完整播放预留切场时间。原始 MP3/WAV 不重新编码。

4-4 保留迷宫规则：第一段上层、第二段下层，漏过路线点会回卷到该段起点。库巴新关初始 5 血，每发火球扣 1，缓存保留已受伤害；斧头直接倒桥是独立通关方式。本轮没有调整其初始血量。

## 手机虚拟按钮

通过鸿蒙公共 NDK `OH_GetDeviceType()` 读取真实设备类型：手机 `phone` 自动显示虚拟按钮；触屏电脑 `2in1`、平板及未知设备默认隐藏。电脑可按 T 手动预览。

- 左侧连续 2×2：上排普通左/右，下排加速左/右，滑动跨格直接切换方向和走跑。按住的高亮边框最后绘制，避免被相邻按钮遮挡。
- 右侧 2×2：上排 JUMP/BIG JUMP，下排 DUCK/FIRE。BIG JUMP 只提高起跳，不增加横向速度，也不与跑跳叠加跳高。
- FIRE 只在火焰形态可用，按一次发射一次，保留冷却和数量限制；触屏加速与大跳不额外开火，键盘和手柄原映射保留。
- 靠近藤蔓时跳跃键兼作向上攀爬，DUCK 兼作向下攀爬和进管道。支持多指同时按住、快速轻点和滑动换键；触摸取消、失焦或视口改变时释放触点。
- 游戏中 II 暂停/恢复；菜单左侧四格为左/右/上/下，右侧上排为 OK/BACK。

## 运行与验证

使用带 C# 的 Godot 4.7.2 编辑器及 .NET SDK 10.0.401。工程使用 `Godot.NET.Sdk/4.7.2-ohos.3`、`net10.0`；配套 NuGet 包保存在 [ThirdParty/GodotNuGet](ThirdParty/GodotNuGet)，包源不依赖作者的个人目录。

```sh
dotnet restore --configfile NuGet.Config
dotnet build --no-restore
```

导入 [project.godot](project.godot)，按 F5，选择 NEW GAME 或 LEVEL SELECT。普通非 .NET 编辑器不能运行本工程。

v0.1.3 普通 .NET 源码构建为 0 警告、0 错误，无窗口机制、输入和音频时序检查 **6366/6366** 通过，见 [完整逻辑报告](Reports/Regression/gameplay-logic-fixes.json)。其中输入专项 235 项、管道专项 2435 项、旗杆底座与清关专项 1675 项、标题和死亡音频专项 90 项。分项包含在总数内，不重复累加。

```sh
godot --headless --path . -- --verify-logic
# 仅检查触控、键盘和手柄输入
godot --headless --path . -- --verify-input
```

本轮按要求没有执行可视化、GPU、截图或音频试听，报告标明 `graphicsIncluded=false`，音频专项另标明 `audioPlaybackIncluded=false`。画面、手感和试听由用户手动确认。v0.1.2 的历史图形结果不作为本版本验收，详见 [验证范围](VERIFICATION.md)。

## 导出未签名 HAP

工具链为 Godot `4.7.2-ohos.8`、.NET SDK `10.0.401-ohos.2`、官方 Command Line Tools `26.0.0.201`。使用相互匹配的 CLI 与 ARM64 Release 模板，NativeAOT 包不需要设备另外安装 .NET SDK。

```sh
python3 Tools/export_hap.py --output Deliverables/mario-csharp-0.1.3-arm64-unsigned.hap
```

默认从 `~/.oheco/packages` 定位工具；其他安装位置可传 `--godot-devkit`、`--dotnet-sdk`、`--command-line-tools`。临时文件使用 `$TMPDIR`，构建产物保存在 `$XDG_CACHE_HOME`。本机构建器需已准备好官方 Hvigor wrapper 缓存。

包名 `org.kdada.mariocsharp`，ARM64 / API 26，版本码 1003。HAP 未签名，安装前需自行签名。导出副本不含 DSH 调试助手。

Editor 和脚本共用 [Mario raw media export 插件](addons/mario_raw_export/plugin.cfg)，按原字节打包 PNG、JSON、音频和 CHR。请保留 [Assets/.gdignore](Assets/.gdignore)，并启用该插件。脚本逐个核对 PCK 中的原始素材及导出清单，确认 CHR 为正确的 8192 字节；手动导出的文件也可检查：

```sh
python3 Tools/verify_export_resources.py Deliverables/mario-csharp-0.1.3-arm64-unsigned.hap
```

## 源码与完成度

- [Scripts](Scripts)：独立 C# 游戏逻辑、JSON 预生成序列化上下文及回归程序。
- [Scenes/Game.tscn](Scenes/Game.tscn)：主场景；[Data](Data)：声明式地图与碰撞；[Assets](Assets)：原始媒体与精灵描述。
- 游戏不引用参考项目运行时代码，不运行下载的 GDScript 游戏控制器。来源、权利归属和第三方许可见 [SOURCES.md](SOURCES.md) 与 [Licenses](Licenses)。

尚未完成从 New Game 到 8-4 结局的连续输入通关或 NES 逐帧对照。当前为单人流程，尚未实现双人轮流、第二轮敌人替换及原硬件全部边缘行为。
