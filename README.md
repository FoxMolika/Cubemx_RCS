# RCS 底盘与位点地图 · CubeMX 合并安装版 0.3.4

**新电脑部署请运行单文件合并安装器：[dist/RCS_CubeMX_Suite_Setup_0.3.4_win-x64.exe](dist/RCS_CubeMX_Suite_Setup_0.3.4_win-x64.exe)。** 安装器内嵌 RCS.Chassis 0.3.4 包、地图编辑器 0.2.0、`RCS_KeilHeaders.exe`、示例和说明，自动定位 CubeMX、调用其原生导入并核验注册与全部文件哈希，不需要手动导入 `.pack`，也不需要 Python。目标电脑需已安装完整 STM32CubeMX（已验证 6.18.1；其他版本尚未承诺兼容）。

**只想要 CubeMX 包**：手动导入 [dist/RCS.Chassis.0.3.4.pack](dist/RCS.Chassis.0.3.4.pack)（与安装器内嵌的包逐字节相同）。在 CubeMX 中 **Help → Manage embedded software packages → From Local** 选择该文件即可；只导入包时不会安装地图编辑器和 Keil 头文件工具。

**主要入口是 CubeMX 原生界面。** 安装后，在 CubeMX 中选择底盘、填写尺寸，点击 Generate Code，直接生成独立的底盘 `.c/.h`。

## 0.3.4：CAN依赖和自动头文件补全

修复用户tester123工程没有配置CAN时出现的CAN_HandleTypeDef等编译错误。安装后点“关联工程…”选择已保存的.ioc：工具合并.extSettings以启用CAN HAL驱动，并在CubeMX的生成后操作中登记工程内RCS_PostGenerate.exe。每个工程只关联一次，后续Generate Code自动登记4个头文件，不需要手动运行脚本。

关联工具支持F407 HAL + Keil MDK，能将已有RCS组件版本切换到0.3.4；保留原.ioc和.extSettings备份，不覆盖已有的其他生成后操作。安装本身不会扫描或改写用户工程。实际硬件CAN1/CAN2引脚、波特率、滤波和中断仍需按接线配置；启用CAN HAL只保证驱动可编译，并不完成硬件初始化。

用户的0.3.3目录和test原工程保留。修复版示例和验收见dist/tester123_fixed_0.3.4.zip与docs/0.3.4修复说明.md。

## 保留的航点动作

选择航点 → “航点动作…”：预留夹爪、舵机、升降的启动、完成条件、完成清理和取消清理。导出独立路径3C3H，支持按稳定航点ID保留USER CODE区域。详见[航点动作说明](map_editor/docs/航点动作说明.md)。默认只是注释入口，需接入自己的驱动。

## 延续的合并与修复

以经过独立复核的 Fix 0.3.1 为基线，保留 Claude 的固件修复。新版使用独立的 0.3.4 包目录，可以与两个不同内容的 0.3.1 包并存，避免升级时同版本内容冲突。已有工程请明确选择 0.3.4，不自动改写 .ioc。

新增持久化安装事务：导入失败时隔离半成品并恢复原包；首次安装失败也能重新尝试；进程意外中断后，下次安装先恢复未完成操作。失败内容保留在 Repository/.rcs-chassis-transactions 下，不覆盖或删除用户修改。

- 速度环微分项改为作用在**测量值**上，消除设定值阶跃引起的微分冲击；Kd 只抑制实际转速变化。
- 总线关闭改为直接读取 `CAN_ESR.BOFF`，不再依赖 HAL 错误中断；配合 CubeMX 的 AutoBusOff 可自动恢复后重新解锁。
- 发送邮箱未清空时先中止旧请求，并最多等待 `RCS_TX_ABORT_WAIT_MS`（默认 2 ms）让在途帧发完；超时才报 `RCS_TX_FAILED`，避免正常发送被误判为故障。
- `rcs_config.h` 增加编译期组合检查（重复电机 ID、超时 ≤ 周期、方向、电流上限、布局、CAN），非法配置在编译时报 `#error "RCS: ..."`，这些配置错误会在编译时报告；实际运动仍需接入任务并调好速度环。
- 三个模块头文件带 `extern "C"`，可被 C++ 文件直接包含。
- 新增 `RCS_KeilHeaders.exe`：把 4 个头文件加入 CubeMX 生成的 Keil 工程树（幂等，首次修改前备份 `.uvprojx.rcs-backup`）。
- 合并安装器：导入时跳过 CubeMX 的网络更新检查，避免全新配置下的 `Updater is busy`（仍保留有界重试）；支持跨卷安装目录、保留其他版本并提示；地图编辑器 0.2.0（导入 `rcs_config.h`、采样间隔/命令超时约束）。

## 安装

1. 保存并关闭 CubeMX。
2. 运行 `dist/RCS_CubeMX_Suite_Setup_0.3.4_win-x64.exe`；未自动找到 CubeMX 时选择本机 `STM32CubeMX.exe`。
3. 选择套件保存目录（默认 `LocalAppData\RCS_CubeMX_Suite_0.3.4`），点击 **安装 / 修复**。
4. 安装器释放内嵌文件、调用 CubeMX 原生导入 `RCS.Chassis 0.3.4`，并同时核验注册 XML 与包内全部文件哈希；两项都通过才显示成功。

安装记录：`install-success.xml`；包所有权收据：`cube-pack-receipt.xml`；导入日志：`import-pack.log`；失败详情：`suite-error.log`。均在所选套件目录。

## 在 CubeMX 中使用

1. 打开 STM32F407 HAL 工程，**Software Packs → Select Components**，展开 **RCS.Chassis 0.3.4**，勾选 **Robotics Chassis**，OK。
2. 左侧 **Middleware and Software Packs → Chassis**，再勾选上方的 **Robotics Chassis** 模式。
3. 在中文“底盘参数”页选择布局、填写轮径/轮距/全向半径、减速比、CAN 与电机 ID、PID 和限制。
4. 保存并关闭.ioc，在安装器点 **关联工程…** 选择该文件（每工程一次）；重新打开.ioc并点击 **Generate Code**。
5. 检查生成文件：

| 文件 | 作用 |
|---|---|
| `rcs_chassis.c/h` | 运动学与轮速换算 |
| `rcs_control.c/h` | 速度闭环、限速、超时、锁定 |
| `rcs_hal.c/h` | STM32F4 HAL CAN 适配与对外接口 |
| `rcs_config.h` | CubeMX 按参数生成的配置头 |

源码路径 `Middlewares/Third_Party/RCS_Robotics/RCS_Chassis/`，配置头由CubeMX工程结构决定，可能位于 `Chassis/`、`Inc/` 或 `Core/Inc/`。组件不向 `main.c` 插入初始化、循环或回调，不创建任务，不接管全局 HAL 回调。

## Keil头文件自动显示

使用安装目录里的RCS_ProjectSetup.exe完成一次工程关联后，CubeMX会在生成结束时自动调用工程内RCS_Tools_034/RCS_PostGenerate.exe，补齐4个头文件。运行结果写入同目录headers-result.txt；Keil已打开时选择重新加载工程。

RCS_KeilHeaders.exe保留作手动诊断备用，不再是正常使用时每次都要运行的步骤。工程移动后，先重新打开.ioc再生成；生成后工具使用相对路径随工程保存。若已有其他生成后操作，关联工具会保留原配置并拒绝覆盖，需要将两项操作合并。

## 接口与接入

生成的是独立模块：`RCS_Init` 绑定 CAN、`RCS_OnCanRx` 接收分发、`RCS_Process` 周期调用（默认 5 ms）、`RCS_Arm` 解锁、`RCS_SetVelocity` 下达速度、`RCS_Lock` 锁定。完整接口、板级 CAN 职责、编译期检查、微分项和发送邮箱说明见 [接入说明](docs/接入说明.md)。

## 验收范围与证据

已验证 **Windows x64 + CubeMX 6.18.1 + STM32F407 HAL + M3508/C620**，支持四麦轮 X 型、等半径切向三全向轮和四全向轮。

- 主机：1555 项运动学/控制检查、HAL 桥接模拟、编译期配置检查、C++ 包含链接检查，见 [build/host_validation.txt](build/host_validation.txt)。
- 合并安装器：隔离配置与空仓库下的安装、重复安装、注册丢失修复、保留既有包、拒绝被修改包、实际卸载、保留用户资料，见 [suite/build/acceptance.json](suite/build/acceptance.json)。
- 原生生成：使用实际安装的 0.3.4 包执行 CubeMX Generate Code，核对布局/轮径和 7 个生成文件、`main.c` 无底盘钩子；结果同上文件。

仍是原型：生成日志保留 `Invalid RefComponent list` 诊断；没有烧录、实车和 ARM 完整链接验证；二进制未做代码签名。详细边界见 [验证记录](docs/验证记录.md)。

## 目录与开发工具

`dist/` 是包与源码 ZIP；`suite/` 是合并安装器源码及其 `dist/` 交付物；`installer/` 是旧的独立安装器源码（历史保留）；`firmware/` 是 C 模板；`pack/` 是包的展开内容；`docs/` 是说明；`keil_headers/` 是 Keil 头文件工具源码；`map_editor/` 是地图编辑器；`tools/`、`tests/` 是开发工具；`build/` 是测试缓存和日志；`native/` 是生成检查样例。

开发检查：

```text
python tools/verify.py             # 主机测试、配置检查、打包一致性
python tools/build_pack.py         # 重建 dist 包与源码 ZIP
python suite/tools/build.py        # 重建合并安装器（复用 dist 的同一个 pack）
python suite/tools/verify.py       # 安装器验收，写 suite/build/acceptance.json
python suite/tools/finish_tests.py # 原生生成、Keil 工具、卸载验收
```

普通用户只需要安装器和 CubeMX，不需要运行这些命令。生成的默认样例不代表实车参数；速度环默认 PID 为零，运行前必须调参并确认轮向。

## 许可

自写模块和工具适用 MIT 许可；ST/CMSIS 文件保留各自许可。
