> 0.5.1新增：地图编辑器“关联工程…”→选择.ioc→“导出 C / H”保持“导出到关联工程”勾选。自动加入Application/foxmolikaRoute中的3C3H与包含目录，CubeMX重生成后自动恢复。保存地图记住关联，旧0.3.6目录保留。

# RCS 合并安装器 0.5.1

单文件 EXE 内嵌底盘包 0.5.1、地图编辑器 0.3.0、工程关联/头文件工具与更新后的网页教程。交付在根目录 dist/，完整操作见 [使用说明](docs/使用说明.html)。

安装完成后，每个工程需主动点击“关联工程…”→“选择 .ioc 并关联 / 升级”；保存关闭后关联，重开 .ioc 再 Generate Code。Keil 重载显示底盘4H。

地图从安装器“启动地图编辑器”或安装目录 foxmolika_MapEditor.exe 打开，是独立窗口。默认目录 %LOCALAPPDATA%\foxmolika_CubeMX_Suite_0.5.1。

源码构建和实际验收由根目录“重新打包和验证.cmd”统一组织。build/acceptance.json、transactions.json 记录实际测试二进制哈希；发布脚本拒绝未通过或哈希不一致的工件。详见根 README 和 docs/验证记录.md。
