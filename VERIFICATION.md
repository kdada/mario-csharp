# 验证记录与范围

## Editor / JIT

- 当前机制回归：750/750，通过，构建无警告或错误。记录见 [verification.json](Reports/verification.json)。
- 原生 OpenHarmony / Vulkan 回归：817/817，通过；包含实际 GPU 帧、手机按键显示/按下反馈及菜单排布。记录见 [gui-verification.json](Reports/gui-verification.json)。
- 手机控件专项：headless 41 项、Vulkan 45 项，覆盖 phone/2in1 分类、多指组合、快速轻点、双拇指跑跳、滑动/取消/失焦释放、菜单方向与确认返回、HUD 不遮挡。记录见 [phone-touch-controls.json](Reports/Regression/phone-touch-controls.json)。
- 当前电脑经系统 NDK 及游戏 C# 均返回 `2in1`，默认不显示虚拟按钮。手机 UI 在本机原生窗口中手动启用并直接向游戏处理器传入 Godot 触摸事件验证；不等同于在实体手机上用手指操作的验收。
- NativeAOT 所需地图与存档 JSON 使用预生成序列化上下文。

## HAP

按用户要求，本次发布不对 HAP 进行包审计、安装、签名或运行测试。发布物仅完成 Godot 官方导出链路的未签名 ARM64 Release / NativeAOT 构建。HAP 安装前需要自行签名。

`Tools/audit_hap.py` 和 `Tools/smoke_hap.py` 是保留的可选开发工具，本次没有执行；不能将它们的存在表述为 HAP 已通过验收。

## 游戏范围

32 个主关和 57 个房间有场景与单项机制覆盖。回归程序会定位测试位置或直接调用机制，不等同于从起点连续操作通关。尚未完成 New Game 至 8-4 结局的连续输入通关，也未完成签名 HAP 中的 Vulkan、音频、物理键盘、手柄或触屏全流程验收。
