# 验证记录与范围

## Editor / JIT

- 当前机制回归：709/709，通过，构建无警告或错误。记录见 [verification.json](Reports/verification.json)。
- 原生 OpenHarmony / Vulkan 回归：772/772，通过；包含实际 GPU 帧的精灵位置、金币高度、管道食人花隐藏和旗杆底座遮挡。记录见 [gui-verification.json](Reports/gui-verification.json)。
- NativeAOT 所需地图与存档 JSON 已改用预生成序列化上下文，并在 Editor/JIT 的 709 项回归中验证。

## HAP

按用户要求，本次发布不对 HAP 进行包审计、安装、签名或运行测试。发布物仅完成 Godot 官方导出链路的未签名 ARM64 Release / NativeAOT 构建。HAP 安装前需要自行签名。

`Tools/audit_hap.py` 和 `Tools/smoke_hap.py` 是保留的可选开发工具，本次没有执行；不能将它们的存在表述为 HAP 已通过验收。

## 游戏范围

32 个主关和 57 个房间有场景与单项机制覆盖。回归程序会定位测试位置或直接调用机制，不等同于从起点连续操作通关。尚未完成 New Game 至 8-4 结局的连续输入通关，也未完成签名 HAP 中的 Vulkan、音频、物理键盘、手柄或触屏全流程验收。
