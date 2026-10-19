# 验证记录与范围

## v0.1.3 无窗口检查

环境为 OpenHarmony ARM64，Godot `4.7.2-ohos.8`（引擎 `a4edc48cb`），.NET SDK `10.0.401`，`Godot.NET.Sdk/4.7.2-ohos.3`、`net10.0`。使用隔离构建缓存，避免与用户正在运行的 Editor 共用 NuGet 中间目录；普通 .NET 源码构建为 0 警告、0 错误。

完整机制、输入及音频状态检查 **6366/6366 通过**。以下分项均包含在总数内：

| 报告 | 通过数 | 范围 |
| --- | ---: | --- |
| [完整逻辑](Reports/Regression/gameplay-logic-fixes.json) | 6366 | 本轮修复与既有机制回归 |
| [触控与物理输入](Reports/Regression/phone-touch-controls.json) | 235 | 多点触控、按下沿、滑动/取消、键盘、手柄 |
| [管道到达](Reports/Regression/pipe-arrival-grounding-swim.json) | 2435 | 57 房间相机、管口条件、泳姿与过渡 |
| [旗杆底座](Reports/Regression/flag-base-collision-clear.json) | 1675 | 22 旗杆房间、三种体型、30/60/120 Hz 碰撞与清关轨迹 |
| [标题和死亡音频](Reports/Regression/title-death-audio.json) | 90 | 原始文件哈希、音频元数据、循环策略、死亡分段和切场时序 |

入口：

```sh
godot --headless --path . -- --verify-logic
```

新增覆盖包括相对脚底接触、水中章鱼与鱼踩踏、植物和火泡周期、接触转向、库巴五点生命和喷火方向、水鱼生成范围/合法高度/密度/离屏清理、有限的一格窄坑支撑、练习形态、弹簧帧序列、迷宫路线与 NPC 单帧绑定。运行中没有写入用户存档。

按用户要求，本版本没有执行可视化测试、GPU/截图回归或音频试听，也没有控制或中断其正在手测的原生 Editor。完整报告含 `graphicsIncluded=false`；音频专项使用禁止实际播放的独立实例，另含 `audioPlaybackIncluded=false`。元数据与时序检查不能替代画面和听感验收，后者由用户手动进行。

## 发布构建

发布使用 Godot 官方 OpenHarmony ARM64 Release / NativeAOT 导出链路，导出成功。PCK 共 1543 项，1488 个 Assets 与 3 个 Data 文件、导出清单及 8192 字节 CHR 均与工程逐字节匹配，见 [发布资源报告](Reports/Regression/v0.1.3-hap-resources.json)。

NativeAOT 导出产生 66 条分析警告，涉及验证/调试报告中的反射 JSON 序列化（IL2026、IL3050）及 GodotSharp 的裁剪/AOT 分析（IL2104、IL3053），没有编译错误。普通 .NET 的零警告结果与 NativeAOT 分别记录；未在 HAP 中运行这些验证/调试报告入口，不能将其视为已通过 AOT 兼容性验收。运行时地图与存档使用预生成 JSON 上下文。

包大小、SHA-256 和下载说明保存在 [v0.1.3 发布说明](Reports/release-v0.1.3.md)。

HAP 未签名，安装前需要自行签名。本轮没有执行 HAP 安装、运行或全包审计；`Tools/audit_hap.py`、`Tools/smoke_hap.py` 为保留的可选工具，其存在不代表本版本完成了设备验收。

## 历史版本

v0.1.2 的无窗口检查为 805/805，原生 Editor / Vulkan 检查为 872/872；图形记录和历史布局见 [v0.1.2 发布说明](Reports/release-v0.1.2.md) 与 [历史截图](Reports/Regression/phone-sidebars-v0.1.2.png)。这些结果属于旧版本，不作为 v0.1.3 新布局、精灵层级或动画的图形验收。

## 游戏范围

32 个主关和 57 个房间有场景及单项机制覆盖。回归程序会定位测试位置或直接调用机制，不等同于从起点连续操作通关。尚未完成 New Game 至 8-4 结局的连续输入通关，也未完成签名 HAP 中的 Vulkan、音频、物理键盘、手柄或触屏全流程验收。
