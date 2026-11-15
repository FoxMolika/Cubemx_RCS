# RCS CubeMX 合并安装程序 0.3.4

交付入口：`dist/RCS_CubeMX_Suite_Setup_0.3.4_win-x64.exe`。此单文件替代之前分别部署的底盘安装器和地图安装器。

把一个 EXE 复制到另一台已有完整 STM32CubeMX 的 Windows 电脑，使用实际运行 CubeMX 的同一用户安装即可。自动定位 CubeMX，找不到时只需选择 `STM32CubeMX.exe`，不需要选择/解压/手动导入`.pack`。

包含：RCS.Chassis 0.3.4 原生 CubeMX 包、地图编辑器 0.2.0、`RCS_KeilHeaders.exe`、离线图文说明、任务接入示例、SDK、MIT 许可及安装/修复逻辑。运行不需要 Python 和网络，不分发 ST 官方 CubeMX 本体。已验证 CubeMX 6.18.1；其他版本未承诺兼容。地图画布仍是独立配套窗口。

## 0.3.4 修复点

## 0.3.4：CAN依赖和自动头文件补全

修复用户tester123工程没有配置CAN时出现的CAN_HandleTypeDef等编译错误。安装后点“关联工程…”选择已保存的.ioc：工具合并.extSettings以启用CAN HAL驱动，并在CubeMX的生成后操作中登记工程内RCS_PostGenerate.exe。每个工程只关联一次，后续Generate Code自动登记4个头文件，不需要手动运行脚本。

关联工具支持F407 HAL + Keil MDK，能将已有RCS组件版本切换到0.3.4；保留原.ioc和.extSettings备份，不覆盖已有的其他生成后操作。安装本身不会扫描或改写用户工程。实际硬件CAN1/CAN2引脚、波特率、滤波和中断仍需按接线配置；启用CAN HAL只保证驱动可编译，并不完成硬件初始化。

用户的0.3.3目录和test原工程保留。修复版示例和验收见dist/tester123_fixed_0.3.4.zip与docs/0.3.4修复说明.md。



- 安装器不再自行生成第二个包：直接嵌入 `../dist/RCS.Chassis.0.3.4.pack`，安装的包与手动导入的包逐字节相同，`dist/SHA256SUMS.txt` 同时覆盖两者来源。
- 安装目录可跨磁盘：仓库在 D:、工具在 C: 时按文件复制并逐字节校验后再删除源目录，失败保留源目录。
- 新配置首次导入时 CubeMX 的网络更新线程会占住 updater 锁，`swmgr install` 返回 `Updater is busy`/KO。导入用的 CubeMX 进程加 `-DMX_UPDATER_SKIP_UPDATE=true` 跳过网络更新（只导入本地已校验的 MIT 包），并保留最多 4 次重试；重试日志保存为 `import-pack-attemptN.log`。
- 安装后提示仓库中其他 RCS.Chassis 版本；旧工程继续使用旧版本，新工程在组件选择器里选 0.3.4。
- 新增 `RCS_KeilHeaders.exe`（源码在 `../keil_headers/`），把 4 个头文件加入 CubeMX 生成的 MDK 工程树。
- 地图编辑器 0.2.0 与底盘 0.3.4 同步。

## 验证

`build/acceptance.json` 记录实际二进制测试；`docs/验证记录.md` 给出验证边界。完整 Windows 重装/另一台实体电脑未测试，不以同机隔离配置代替该结论。

从源码包开始时，在工程根目录按顺序执行：

```powershell
python map_editor/tools/build.py  # 地图编辑器 0.2.0（源码变动时）
python tools/build_pack.py        # 重建 dist/RCS.Chassis.0.3.4.pack 与源码 ZIP
python suite/tools/build.py       # 嵌入同一个包并生成安装器
python suite/tools/verify.py
python suite/tools/finish_tests.py
python suite/tools/release.py
```

`verify.py` 与 `finish_tests.py` 顺序执行：后者使用前者实际安装的配置做原生生成、Keil 工具和卸载检查。构建需要 Windows .NET 4 的 csc、Python 以及 CubeMX 内置 javac；部署时均不需要额外开发工具。原生代码生成验收另需本机已有 `STM32Cube_FW_F4_V1.28.3` 固件库。

静默安装：`/S /D=<目录> [/CUBEMX=<CubeMX目录或EXE>]`。自动化测试另用 `/TESTHOME=<全新测试配置>` 隔离文件和 Java 偏好设置；生产安装使用真实当前用户配置。卸载：`/S /UNINSTALL /D=<原目录>`。错误日志可由 `/LOG=<路径>` 指定。
