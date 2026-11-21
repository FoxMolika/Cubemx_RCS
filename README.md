# foxmolika 0.8.4（本地硬件模板版）

独立项目，基于0.8.3.beta继续实现；旧目录和队内ZIP不改动，不推送、不发布、不自动烧录。

先读 [统一使用说明](docs/0.8.4使用说明.html)；调试时看[调试变量与错误码](docs/调试变量与错误码.md)，所有 Watch 变量名和故障码取值都在那里。新入口为工具栏“硬件管理…”：添加队内/基础设备实例、编辑参数、创建/导入/导出个人模板，在航点和状态机中选择硬件操作。main仍只接App_Init/App_Task一次。

当前自动接入：队内无传感器气缸、队内JY901，以及GPIO/PWM基础模板；用户可导入或编写C/H/C++和生命周期。队内其它电机及STD库留作来源参考，尚不应称为全部自动适配。没有独立定位硬件时纠错留空。

基础路线仍19/21文件，硬件输出到Application/foxmolikaHardware，自动登记C/C++和头文件。已修改文件不覆盖，取消设备保留归档，跨文件失败有恢复记录。

## 本地构建与验证

```text
python tools/validate_beta.py
```

需要Windows/.NET Framework编译器、GCC/G++；原生验收另需本机CubeMX6.18.1、F4 V1.28.3、Keil ARM6.22。队内C++适配需要完整C++库和至少0x2000堆，不能使用MicroLIB。所有验收只在本项目build内的隔离配置中安装生成，不烧录。

交付入口为`交付/使用说明.html`与两个0.8.4单文件安装器。完整验证摘要和本轮JSON证据随交付保存，主机模拟、ARM编译和实车证据分开记录。

## 代码入口

- map_editor/src/HardwareModel.cs、HardwareCatalog.cs：模板/实例、参数、资源与快照。
- HardwareEditor.cs、HardwareTemplateEditor.cs、HardwareOperations.cs：GUI和动作绑定。
- HardwareGenerator.cs、HardwareIntegration.cs：真实驱动生成和App生命周期。
- map_editor/hardware：首批驱动和薄适配；reference：两队内ZIP的来源摘要与原始参考。
- map_editor/templates/rcs_app_task.*.tpl：应用调度与只读调试快照 `g_dbg`（每次 App_task 刷新）。
- HardwareExport.cs、keil_headers/HardwareBinding.cs：交付、Keil登记、归档与恢复。
- tools/test_hardware_*.py、test_beta_compatibility.py、test_beta_native.py：本轮回归。
