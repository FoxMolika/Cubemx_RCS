> 0.3.7新增：地图编辑器“关联工程…”→选择.ioc→“导出 C / H”保持“导出到关联工程”勾选。自动加入Application/foxmolikaRoute中的3C3H与包含目录，CubeMX重生成后自动恢复。保存地图记住关联，旧0.3.6目录保留。

# foxmolika · CubeMX 底盘与地图套件 0.3.7

基于已验收的RCS 0.3.5改名为foxmolika，新目录独立交付，保留原 0.3.3、0.3.4 和 DeepSeek/Fix 目录。底盘包 0.3.7，地图编辑器 0.3.0；本版不含 0.4 的 CubeMX 内嵌绘图实验。

运行 [单文件安装器](dist/foxmolika_CubeMX_Setup_0.3.7_win-x64.exe)。完整步骤见 [网页教程](docs/使用说明.html)，实际差异见 [DeepSeek 版本对比](docs/与DeepSeek版本对比.md)。目标电脑需完整 CubeMX，已验证 6.18.1；用户运行不需要 Python 或 clang-format。

改名范围与旧工程切换步骤见 [0.3.7改名说明](docs/改名说明_0.3.7.md)。保留RCS_*接口和rcs_*.c/.h文件名；CubeMX组件、安装器和编辑器使用foxmolika。旧RCS组件与新组件不可同时启用。

## 必须先关联，再生成

1. 安装器点击“安装 / 修复”。默认保存目录为 `%LOCALAPPDATA%\foxmolika_CubeMX_Suite_0.3.7`。
2. CubeMX 的 Software Packs → Select Components 选择 RCS.foxmolika 0.3.7；在 Middleware and Software Packs → foxmolika 启用 Robotics foxmolika。配置参数，保存 .ioc 并关闭工程。
3. 安装器最右侧点 **“关联工程…”** → **“选择 .ioc 并关联 / 升级”** → 选择刚保存的 .ioc。也可直接运行安装目录的 foxmolika_ProjectSetup.exe。**每个新工程都要主动关联一次。**
4. 重新打开该 .ioc，再 Generate Code。Keil 接受重载后，底盘分组显示 3C4H。结果见 `foxmolika_Tools_037/headers-result.txt`。

安装 .pack 不代表工程已关联。工具适用 F407 HAL + MDK-ARM，保留 .ioc/.extSettings 备份和其他生成后操作；硬件 CAN 引脚、波特率、滤波、中断仍需自己配置。main.c 不插入底盘逻辑。

## 地形／地图／路径编辑器在哪里？

它们是同一个独立 Windows 程序。安装器点 **“启动地图编辑器”**，或打开安装目录的 **foxmolika_MapEditor.exe**。默认路径 `%LOCALAPPDATA%\foxmolika_CubeMX_Suite_0.3.7\foxmolika_MapEditor.exe`。当前未嵌入 CubeMX 窗口。

编辑地图、朝向和航点动作后，导出独立 `rcs_route.c/h`、`rcs_nav.c/h`、`rcs_actions.c/h`（3C3H）。地图编辑器关联同一工程后，工程模式导出会自动登记路径3C3H和包含目录；任务调用仍在自己的任务文件里接入。用 `rcs_route_init` 绑定动作，USER CODE 支持保留、冲突拒绝。详见 [地图说明](map_editor/docs/使用说明.html) 与 [动作说明](map_editor/docs/航点动作说明.md)。

## 沿用0.3.5的生成方式

采用 DeepSeek 六个固件文件的中文 Doxygen、分区注释和可读排版，保留公开 RCS API。在此基础上保留 0.3.4 的 CAN 依赖与工程关联修复。新增配置共享模板和内嵌路径/动作模板，覆盖 13 个输出 C/H；开发侧检查语句、缩进、相邻函数文档，手写 USER CODE 区域不重排。离线导出与 CubeMX 的 rcs_config.h 共用宏前缀和约束段。

原队 HAL 与 STD 库风格、RTOS 架构不同。本版仍为 F407 HAL 纯 C 模块，没有改成队内 C++ 电机类或 UCOS/FreeRTOS 架构；可读性统计不能作为功能相似度百分比。

## 文件和验证

`dist/`：单文件安装器、pack、默认7文件C/H包、全源码ZIP、修复示例工程ZIP和网页教程。`firmware/`、`templates/`、`map_editor/templates/` 是生成源；`tools/`、`tests/`、`suite/tools/` 是开发验收工具。

[底盘接入](docs/接入说明.md)；[验证说明](docs/验证记录.md)。最终发布要求：主机1555项、HAL桥接、C++链接、编译期非法配置、地图72项/路径模拟/动作349项、17个风格与导出兼容场景、安装事务、原生生成、每次重生成自动登记4H、Keil ARM Compiler6.22完整Rebuild与链接0错误0警告。

源码开发可运行 `重新打包和验证.cmd`。需要 Python、主机C/C++编译器、完整CubeMX6.18.1、Keil6.22；这些不是普通用户运行依赖。旧版实际导出样本包含在 tests/fixtures/style 中，其他版本源码目录不是重建的必要依赖。风格描述统计见随源码保存的验收JSON。

未烧录、未实车测试，未在另一台物理电脑或全新Windows虚拟机验收；二进制未签名。默认参数不是实车标定值，PID需调参。自写模块MIT许可，ST/CMSIS保留原许可。
