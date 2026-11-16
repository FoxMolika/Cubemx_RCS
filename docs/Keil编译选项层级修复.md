# Keil目标级编译选项修复（2026-11-16）

问题来自 `ProjectBinding.UpdateRoute` 的 `Descendants("Cads")`：文件和分组的覆盖选项被当成多个目标选项。`SingleOrDefault` 因此报错。`KeilHeaders` 的 `FirstOrDefault` 也有层级歧义，可能读到文件级空包含路径。

两处现在共用 `TargetCompilerOptions`，只读 `Target/TargetOption/TargetArmAds/Cads/VariousControls`。每个直接层级必须恰好一个；缺失/重复给出中文错误，不取任意第一个。有效文件/分组覆盖选项保持原样。

验证：TEST4的真实副本连续三次关联和导出、重复运行生成后助手均成功，路径只登记一次，原TEST4的IOC、uvprojx和助手哈希不变。仓内加入从TEST4提取并替换路径的FileOption结构，测试平地6文件、坡道8文件、多目标、头文件定位、各级缺失和重复，以及失败保留工程。先用修复前代码复现异常，再验证修复。新增13项及已有41+13+21项关联/事务回归通过。

运行：`python map_editor/tools/build.py` 后执行 `python tools/test_keil_options.py`。已加入 `tools/check.py` 的host检查。

本次保留0.8.0程序集版本，GitHub原Release不会自动更新。已有工程目录的 `foxmolika_Tools_080/foxmolika_PostGenerate.exe` 仍是旧副本；仅更新地图编辑器不能替换它。使用修复包时请先保存并关闭相关软件，备份旧文件，再把包内tools中的助手复制到工程的foxmolika_Tools_080目录；editor中的编辑器和关联工具复制到套件安装目录。三个EXE必须使用同一修复包。之后重新关联IOC并在Keil重新加载工程。不要还原或删除原uvprojx来绕过错误。

验证摘要见 [keil-options-20261116.json](validation/keil-options-20261116.json)。本次未烧录或控制车辆。
