# foxmolika 0.8.8

0.8.8 在导出时判断 UART 和 TIM 回调归属，兼容标准 CubeMX 定时器时基和自行接收数据的个人 UART 驱动。0.8.7 助手和已知 0.8.5 修复助手可通过 SHA-256 校验升级。

先读[用户手册](docs/用户手册.html)。调试时可查阅[调试变量与错误码](docs/调试变量与错误码.md)。使用工具栏“硬件管理…”添加设备、编辑参数或导入个人模板，再在航点和状态机中选择硬件操作。main 只需接入 App_Init/App_Task 一次。

当前自动接入：队内无传感器气缸、队内JY901、队内维特陀螺仪（XS_spin8 连续航向），以及GPIO/PWM基础模板；用户可导入或编写C/H/C++和生命周期。队内其它电机及STD库留作来源参考，尚不应称为全部自动适配。没有独立定位硬件时纠错留空。

基础路线仍19/21文件，硬件输出到Application/foxmolikaHardware，自动登记C/C++和头文件。已修改文件不覆盖，取消设备保留归档，跨文件失败有恢复记录。

## 本地构建与验证

```text
python tools/validate_beta.py
```

需要Windows/.NET Framework编译器、GCC/G++；原生验收另需本机CubeMX6.18.1、F4 V1.28.3、Keil ARM6.22。队内C++适配需要完整C++库和至少0x2000堆，不能使用MicroLIB。所有验收只在本项目build内的隔离配置中安装生成，不烧录。

完成本地交付构建后，可在 `交付/使用说明.html` 查看使用说明，一体安装包名称为 `foxmolika_CubeMX_Setup_0.8.8_win-x64.exe`。

## 代码入口

- map_editor/src/HardwareModel.cs、HardwareCatalog.cs：模板/实例、参数、资源与快照。
- HardwareEditor.cs、HardwareTemplateEditor.cs、HardwareOperations.cs：GUI和动作绑定。
- HardwareGenerator.cs、HardwareIntegration.cs：真实驱动生成和App生命周期。
- map_editor/hardware：首批驱动和薄适配；reference：两队内ZIP的来源摘要与原始参考。
- map_editor/templates/rcs_app_task.*.tpl：应用调度与只读调试快照 `g_dbg`（每次 App_task 刷新）。
- HardwareExport.cs、keil_headers/HardwareBinding.cs：交付、Keil登记、归档与恢复。
- tools/test_hardware_*.py、test_beta_compatibility.py、test_beta_native.py：本轮回归。
