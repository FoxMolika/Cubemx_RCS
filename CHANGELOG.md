# 更新日志

RCS 底盘与位点地图 · CubeMX 套件（foxmolika）。仓库把每次发布保存为一个 tag，可逐版查看代码差异。

- 版本号沿用工程内部编号：0.3.3 → 0.8.2
- 每个 tag 对应一个 GitHub Release，安装器与 .pack 在 Release 附件中
- v0.3.3 是本仓库 Git 历史的起点
- 各版正文摘自该版 变更说明_<版本>.md，原文未改写

## 0.8.4.beta：本地硬件模板版（2026-11-21）

- 从 0.8.4.alpha 提升为本地 beta 线；版本号、助手目录、使用说明与验收脚本统一改为 _084_beta。
- 工具栏“硬件管理…”：队内无传感器气缸、队内 JY901、GPIO/PWM 基础模板，实例与个人模板可导入导出。
- 硬件输出到 Application/foxmolikaHardware，自动登记 C/C++ 与头文件；已修改文件不覆盖，取消保留归档。
- 新增 [调试变量与错误码](docs/调试变量与错误码.md)：Watch 变量分布、六张故障码对照表与排查路径。
- 生成器新增只读调试快照 `g_dbg`（声明在 `rcs_app_task.h`，每次 `App_task()` 刷新）：
  一次展开即可看应用状态、五类首次故障、位姿与各轮收帧年龄，不受优化等级影响。
  无硬件地图仅 `rcs_app_task.c/.h`、`rcs_feedback_port.c/.h` 与 `rcs_can_port.c` 五个文件按设计变化，
  其余仍与 0.8.3.beta 逐字节相同；兼容性用例改为逐文件比对并单独核对新增接口，
  不再整目录一票否决。
- `g_dbg` 追加 `rx_count`、`loop_ms`、`loop_max_ms`、`gap_us_last/max`、`dwt_ready`：
  可区分“帧没到”“帧到了被锁存丢弃”“主循环停顿”三种情况。
- 帧间隔改用 DWT 周期计数测微秒（判据仍按毫秒，收紧为 0）；`gap_us_max` 直接给出空窗长度。
- `rcs_can_port.c` 增加弱定义 HAL 接收回调兜底：统一分发器缺失时中断仍能排空 FIFO，
  不再静默退化成主循环轮询。

详情见 [0.8.4.beta 使用说明](docs/0.8.4.beta使用说明.md)。

## 0.8.2：发布来源一致性（2026-11-16）

- 干净标签构建、每步源码身份检查、Git归档源码包及逐字节核验，绑定全部验收与上传清单。
- 上传前核对真实远端标签及实际资产名称；保留v0.8.0/v0.8.1标签和附件。
- 增加官方081助手的哈希迁移与未知助手拒绝回归；修订代码梳理文档。

详情见[0.8.2变更](变更说明_0.8.2.md)和[版本与发布](docs/版本与发布.md)。

## 0.8.1：工程关联、事务更新与版本收敛（2026-11-16）

- 修复工程含文件级/分组级 `Cads` 时重复关联抛出“序列包含不止一个元素”；关联与头文件助手共用严格目标级读取，保持覆盖选项并拒绝真实重复目标配置。TEST4真实副本连续三次关联/导出通过，另增13项回归。

- 将坡道源码生成的字符串补丁改为具名模板插槽，拆分参数校验、采样与代码组合；补充中文约束说明。
- 修复 `rcs_nav_reset` 中坡道状态复位补丁未命中的缺陷；保留新鲜反馈和人工启动要求。
- 将形状画布交互与窗口命令分层，保留原有编辑、吸附和撤销行为。
- 合并构建公共函数/资源清单，提供 host/native/release 分层检查；移除两份无引用的一次性发布改写脚本。
- 增加基线输出比较及模板失配检查；构建示例写入缓存，避免修改仓内参考地图。

详情见[审查方案与验收记录](docs/代码审查与重构方案.md)。本节记录v0.8.1的变化；旧0.8.0附件保持原样。

- 版本收敛至tools/version.py，构建C#常量、软件包元数据、安装清单和当前教程；新增哈希校验的旧080助手迁移。
- 导出目录、安装暂存和原子文件提交分别增加有限重试与确定性故障回归；1175替换失败先核对内容再重试，不覆盖竞争修改。

## 版本一览
| 版本 | 主题 | 代码 | 对比上一版 |
| --- | --- | --- | --- |
| `0.8.2` | 发布来源一致性 | [tree](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.8.2) | [v0.8.1 → v0.8.2](https://github.com/FoxMolika/Cubemx_RCS/compare/v0.8.1...v0.8.2) |
| `0.8.1` | 工程关联与事务更新修复 | [tree](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.8.1) | [v0.8.0 → v0.8.1](https://github.com/FoxMolika/Cubemx_RCS/compare/v0.8.0...v0.8.1) |
| `0.8.0` | 坡道执行与形状编辑 | [tree](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.8.0) | [v0.7.0 → v0.8.0](https://github.com/FoxMolika/Cubemx_RCS/compare/v0.7.0...v0.8.0) |
| `0.7.0` | 可选硬件纠错模板 | [tree](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.7.0) | [v0.6.1 → v0.7.0](https://github.com/FoxMolika/Cubemx_RCS/compare/v0.6.1...v0.7.0) |
| `0.6.1` | 连续跨平台 | [tree](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.6.1) | [v0.6.0 → v0.6.1](https://github.com/FoxMolika/Cubemx_RCS/compare/v0.6.0...v0.6.1) |
| `0.6.0` | 一体编辑 | [tree](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.6.0) | [v0.5.2 → v0.6.0](https://github.com/FoxMolika/Cubemx_RCS/compare/v0.5.2...v0.6.0) |
| `0.5.2` | 权限修复 | [tree](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.5.2) | [v0.5.1 → v0.5.2](https://github.com/FoxMolika/Cubemx_RCS/compare/v0.5.1...v0.5.2) |
| `0.5.1` | 地形表面直接拖动 | [tree](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.5.1) | [v0.5.0 → v0.5.1](https://github.com/FoxMolika/Cubemx_RCS/compare/v0.5.0...v0.5.1) |
| `0.5.0` | 双动作框与立体地图首版 | [tree](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.5.0) | [v0.4.1 → v0.5.0](https://github.com/FoxMolika/Cubemx_RCS/compare/v0.4.1...v0.5.0) |
| `0.4.1` | 地图直接显示车头与路径夹角 | [tree](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.4.1) | [v0.4.0 → v0.4.1](https://github.com/FoxMolika/Cubemx_RCS/compare/v0.4.0...v0.4.1) |
| `0.4.0` | 航点分阶段与量角器 | [tree](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.4.0) | [v0.3.7 → v0.4.0](https://github.com/FoxMolika/Cubemx_RCS/compare/v0.3.7...v0.4.0) |
| `0.3.7` | 底盘与路径共用工程关联 | [tree](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.3.7) | [v0.3.6 → v0.3.7](https://github.com/FoxMolika/Cubemx_RCS/compare/v0.3.6...v0.3.7) |
| `0.3.6` | 项目改名说明 | [tree](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.3.6) | [v0.3.5 → v0.3.6](https://github.com/FoxMolika/Cubemx_RCS/compare/v0.3.5...v0.3.6) |
| `0.3.5` | 固件风格统一与教程重写 | [tree](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.3.5) | [v0.3.4 → v0.3.5](https://github.com/FoxMolika/Cubemx_RCS/compare/v0.3.4...v0.3.5) |
| `0.3.4` | CAN HAL 依赖与 Keil 头文件自动化 | [tree](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.3.4) | [v0.3.3 → v0.3.4](https://github.com/FoxMolika/Cubemx_RCS/compare/v0.3.3...v0.3.4) |
| `0.3.3` | 航点动作预留 | [tree](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.3.3) | 首个版本 |

## 各版要点

### 0.8.0 · 坡道执行与形状编辑

新增真实rcs_terrain.c/.h，坡道路线4C4H、平地3C3H；标定行程投影、连续过渡、前视限速、主动制动驱动契约。基础模型无需IMU，但启动须实测确认制动并绑定真实非阻塞回调。不增加接边任务点或隐式纠错，支持平台显式转向；不含真实打滑、摩擦/扭矩或俯仰融合。

平台和直坡支持凸多边形轮廓、预设、拖顶点/边/旋转柄、加删点、精确旋转及全局撤销。实际轮廓参与连接、碰撞、净空和执行导出。v6防止旧版忽略；旧版目录和用户工程不覆盖。

工程关联支持有归属和签名校验的6↔8事务更新，自动登记源/头和再生成恢复；完整模块修订检查及并发地图保护。任务侧新增坡道停止保持条件示例，必须合并到已有任务，不能把零电流当成驻坡。教程、截图、示例和单文件安装器同步。

[发布说明](https://github.com/FoxMolika/Cubemx_RCS/releases/tag/v0.8.0) ｜ [代码](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.8.0)

### 0.7.0 · 可选硬件纠错模板

航点流程新增独立纠错列表；导入硬件档案后按有效轴提供刷新估计、XY移动对齐和朝向旋转对齐。没有独立观测时空组直接跳过，旧默认位置纠错和目标朝向二次补偿不再由生成器执行。旧CorrectionMode保留查看，v3/v4原地图不改写；新硬件地图v5避免旧版忽略。

新增真实独立观测读取契约与逐点纠错表。无数据、缺轴、低质量、过期、回放、跳变、主动对齐超时或漂移过大时零输出并锁定故障；不伪造成功。保持机构动作不重放、USER CODE保留与路径3C3H自动登记。

导入仅是硬件能力声明，不是完整型号驱动或总线自动识别。附带档案与适配器示例默认未接入，不能直接启用。旧手写C接口兼容行为与新生成绑定方式在接入说明中区分。立体路线仍只做几何规划，不输出坡道固件。旧版目录、用户工程保留。

[发布说明](https://github.com/FoxMolika/Cubemx_RCS/releases/tag/v0.7.0) ｜ [代码](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.7.0)

### 0.6.1 · 连续跨平台

修复旧版每段最多跨两个表面的限制。航段沿直线自动解析显式连接的地面、坡道、平台链，不再要求在坡道放任务航点。交界只做内部几何分段，不执行动作、纠错或停车。

目的航点属性新增入段地形策略和入段表面链。默认沿连接表面；明确桥下路线可保持起点层。中间表面的限速、坡度、障碍、净空和车体包络全部检查。侧视和三维预览显示完整起伏；同在地面的两端也不会掩盖中间高台。

“立体示例”改为两个地面航点连续上下平台，可在安装目录 examples/连续通行示例.rcsmap.json 打开。旧v3/v4仍可读，默认策略不增加旧JSON字段；旧版目录和用户工程不修改。只升级已通过哈希检查的官方0.6.0生成后工具。

范围：仍是立体地图几何规划，尚未生成实车坡道控制。不是自动寻路；拐弯需在水平平台加转向航点。平地C/H生成和现有硬件驱动不变。安装器是内嵌pack的单文件EXE，保留普通用户Medium安装修复。

新增67项连续通行与真实控件事件检查，包括入段策略全局撤销/重做、端点同层但中间起伏、链顺序无关、歧义、断链、桥下净空、中间障碍/限速、零高度平台回到平地导出。安装器对CubeMX卸载时明确的Updater is busy追加最多三次有限重试；包发生变化或其他错误仍保留记录并报错。

[发布说明](https://github.com/FoxMolika/Cubemx_RCS/releases/tag/v0.6.1) ｜ [代码](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.6.1)

### 0.6.0 · 一体编辑

用户原先在模态地形窗口配置平台后，因为跨坡道路线缺点而无法应用，同时又不能回主窗口加点。新版本移除用户流程中的地形模态入口，把全部操作放在主窗口。

上方常驻＋平台、＋坡道、移动表面、连接、检查、当前层。左侧底盘/平台坡道/连接，中央俯视/侧视/三维/分段参考/检查，右侧航点顺序/表面归属/派生Z/动作及参数。选择前点、切当前层、＋航点，在选中点之后插入坡道点，保留原稳定ID。新坡道点默认关闭纠错，无动作。

同一文档、保存入口和全局撤销/重做；所有视图、属性和表格在撤销或恢复后重绑，编辑直接进入恢复快照。表面拖动、携带本面对象、网格吸附和取消拖动保留。连接可以先记录为草稿，未接合明确告警；未连完可保存重开，不再有应用门槛。结构/非有限值/零尺寸等不安全输入仍拒绝。

完整检查与导出/动态预览门禁保留，立体路线仍不生成坡道控制。平地六个C/H字节回归、所有原95项地形/交互检查继续执行。新增41项实际主窗口控件流程检查，包括真实补坡道点和连接后路线检查通过、草稿保存重开、跨层点选、全局Undo/Redo、表面移动取消和属性重绑。

保留0.5.2的Medium发布标签、非管理员安装、权限预检和错误诊断。单文件安装器内嵌包和教程，验收包含实际Medium安装、CubeMX原生生成/再生成与Keil编译。未烧录、未实车测试、未在第二台实体电脑测试。所有文件在新0.6.0目录，旧版和用户工程未覆盖。

[发布说明](https://github.com/FoxMolika/Cubemx_RCS/releases/tag/v0.6.0) ｜ [代码](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.6.0)

### 0.5.2 · 权限修复

已确认旧发布目录的EXE继承了Low完整性标签，而用户安装目录DACL授予当前账号完全控制。实际对照：相同测试程序在Low模式下写普通用户目录被拒绝，Medium非管理员模式可写。

构建与发布对新EXE设置并读取验证Medium标签，同时验证DACL未变。不改整个工作区、旧版、用户AppData或系统ACL。manifest保持asInvoker。安装器检查实际进程完整性级别，Low时给出明确提示；提供不保存NTFS标签的兼容安装器ZIP，在正常用户目录解压运行。

增加工具/暂存父目录、所需CubeMX仓库、事务和注册路径写入预检；实际不可写时先拒绝提取并给出路径。不自动更改仓库位置或ACL，不在所有情况下强制管理员运行。

CubeMX导入工作目录、Java临时目录和子进程TEMP/TMP改为自有运行目录，user.home保留原用户，完成后清理。启动互斥锁按用户区分，异常进入错误报告。日志无法写指定位置时尝试当前用户诊断目录、临时目录与EXE旁，显示实际路径。

实际Medium、非管理员、非AppContainer进程完成隔离配置下的原生安装、文件/注册核验、编辑器95项检查和卸载；测试拒写工具目录/仓库时在释放载荷前失败且能保存诊断。仅在自有测试树临时构造并还原ACL，不改用户真实CubeMX配置。完整结果见发布核验JSON。未在第二台实体电脑测试，未烧录。

[发布说明](https://github.com/FoxMolika/Cubemx_RCS/releases/tag/v0.5.2) ｜ [代码](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.5.2)

### 0.5.1 · 地形表面直接拖动

新增地形窗口俯视图直接拖动平台和坡道，左侧列表与属性同步，选中表面优先解决上下层重叠。地面固定，点击航点仍为选择节点。

默认网格吸附，Shift临时取消；场地边界优先。默认携带本面SurfaceId的航点/障碍，同一平移量保持坡道局部坐标和规划高度；可关闭联动。不移动相邻连接面。精调X/Y仍只修改几何。

鼠标松开提交一次本地历史记录并检查连接；允许继续调整临时断开的连接，应用时严格校验。Esc、右键、捕获丢失或画布尺寸变化回滚；三维拖动仍旋转相机。窗口内撤销/重做支持Ctrl+Z/Y，包含拖动及其他已提交编辑；取消窗口不修改主地图。

新增38项实际鼠标事件/移动/对话框集成检查，与原57项地形和双框检查共同执行。原平地C/H字节回归和安装器、CubeMX、Keil验收继续执行，结果见发布JSON。未烧录、未实车测试，立体路线仍不生成坡道控制固件。

所有工作位于新0.5.1目录；0.5.0和其他旧版本保持不变。官方0.5.0工程工具支持核验哈希后升级，保留旧文件。中文教程加入直接拖动的步骤和实际窗口截图。

[发布说明](https://github.com/FoxMolika/Cubemx_RCS/releases/tag/v0.5.1) ｜ [代码](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.5.1)

### 0.5.0 · 双动作框与立体地图首版

所有变更在新目录，保留0.4.1及其他旧版本、用户工程。

## 航点双动作框

“航点流程…”同时显示“① 到达航点后”和“③ 离开航点前”，各有自己的动作列表、类型选择、添加/编辑/排序/删除。中间“② 位置纠错”独立配置，不再通过下拉框切换动作组。主表同步分成两列；取消编辑保留原数据。执行调度与原有六个C/H保持兼容。

## 立体地图首版

- 轴对齐矩形水平平台、沿X/Y直坡道，稳定ID、尺寸、高度、限速、向下厚度。
- 航点与障碍分别归属地面/桥面/坡道；规划Z由表面和XY推导。
- 显式表面连接且须等高接边；XY重叠不会自动连通。每段最多跨一次接合，多面需增加航点。
- 主窗口米色栏：地形编辑、立体示例、当前编辑表面；加点/拖动按当前层处理。
- 地形窗口：俯视、累计水平距离—高度侧视、可旋转缩放的三维几何预览，点击节点联动选择。
- 检查范围、引用、连接、断层/空隙、车体包络、坡度、限速、净空；净空计入车高、余量、向下厚度和斜顶面包络。
- 地形为v4；原v3平地数据不加多余字段，旧v1/v2迁移继续支持。

## 控制边界

不生成坡道控制固件，没有高度闭环、IMU融合、自动寻路、台阶或升降平台通行控制。含坡道/高台路线拒绝导出C/H与平地动态预览；地面路线通过检查后仍可导出。三维画面绘制上表面，不是动力学仿真；配置参数须按真实硬件填写。

## 安装与验证

单文件安装器内嵌软件包、编辑器、关联工具与中文教程。官方0.4.1工具核验SHA256后可升级，旧文件保留。教程加入双框、地形步骤和实际窗口截图。

运行 python tools/accept_050.py，全部验证后才发布。包括地形/双框检查、平地C/H字节回归、安装器事务、CubeMX原生生成、Keil编译。未烧录、未实车测试、未在第二台实体电脑测试。最终结果见dist发布核验JSON。

[发布说明](https://github.com/FoxMolika/Cubemx_RCS/releases/tag/v0.5.0) ｜ [代码](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.5.0)

### 0.4.1 · 地图直接显示车头与路径夹角

2026-11-14。在新目录开发，0.4.0、0.3.7 和用户工程保留。

- 默认给每个出发点绘制计划出发车头的橙色虚线、与下一段路径的角度弧，以及顺/逆时针度数。无需打开量角器。
- 使用动作执行后的计划朝向，拖动点、编辑初始朝向和旋转动作后重新计算；不把轨迹方向冒充车头，不把夹角当作必须纠正的误差。
- 选中点优先、标签尽力避让节点/路径、限定画布内位置；支持“显示夹角”开关，不修改地图或生成代码。
- 无下一段不标出发角；重合段与无效引用不显示虚假的0°，绘图不因无效朝向崩溃。
- 补充 ±180°、顺逆时针屏幕转换及独立位图像素检查。运动学、导航和控制 C/H 与0.4.0保持相同。
- 安装器、教程、组件版本升级0.4.1，接受官方0.4.0生成后工具的精确哈希升级，保留旧helper。

验收结果见dist/发布核验_0.4.1.json。未烧录、未实车测试；仅显示计划信息，不代表测得了车辆实时姿态。

[发布说明](https://github.com/FoxMolika/Cubemx_RCS/releases/tag/v0.4.1) ｜ [代码](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.4.1)

### 0.4.0 · 航点分阶段与量角器

2026-11-14。在新目录开发，原 0.3.7 与 0.3.7test 不改动。

1. 航点由隐含同时移动/转向改为固定阶段，支持两组顺序动作及中间纠偏。旋转绝对/相对目标在开始时锁存，等待与机构动作非阻塞；阶段、动作和首发故障可查询。
2. v3 地图及稳定动作 ID。旧旋转、机构、停留映射到相应阶段并另存；USER CODE 合并继续校验生成区域，不覆盖外部修改。
3. 新增量角器：画布双点、交换 A/B、轴向角度、网格夹角、参考朝向、相对旋转、引用点或固定角度、插入两组动作。
4. 预览分阶段执行，正常行驶不插值转头；机构操作显示占位，不假装验证硬件完成。
5. 崩溃时本地 ZIP、完整路径、复制/打开文件夹/关闭；恢复快照独立本地保存，IO 失败显示真实结果。无上传功能。
6. 保存/导出状态及路线摘要；关联导出登记 Keil 构建前版本检查。保存地图后没重新导出会拒绝构建，不静默编译旧路线。
7. 关联升级接受经哈希核对的官方 0.3.6/0.3.7 helper，新 helper 路径 foxmolika_Tools_040；保留外部命令与备份。原有底盘 3C4H、地图 3C3H 登记机制保留。
8. 教程同步说明打开编辑器、主动关联工程、两组动作、量角器、导出、版本检查和 ZIP 报告路径。发布仍使用一个自带 .pack 的安装 EXE。

底盘协议和硬件支持范围没有因本次地图重构自动扩大。旋转与纠偏分离解决命令混合；不承诺消除实际打滑或轮速未跟随。各项测试、原生生成、ARM 链接与未测试边界见发布核验 JSON，不把主机仿真描述成实车试验。

[发布说明](https://github.com/FoxMolika/Cubemx_RCS/releases/tag/v0.4.0) ｜ [代码](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.4.0)

### 0.3.7 · 底盘与路径共用工程关联

在新目录发布，保留0.3.6。底盘包0.3.7，地图编辑器0.3.0；公开C接口和六个底盘固件文件保持原样。

地图编辑器加入“关联工程…”与目标工程栏，和安装器共用ProjectSetup逻辑及同一个内嵌生成后工具。工程模式导出到Application/foxmolikaRoute，自动登记route/nav/actions三组C/H和包含路径。第一次Keil工程还未生成时可先保存源码，Generate后自动加入；再生成会补回被删除的登记。任务与硬件调用仍由用户接在自己的任务模块，不修改main.c。

地图可选ProjectIoc字段保存相对工程路径，旧地图无该字段照常使用。另存地图重新计算基准，跨盘时用绝对路径；打开地图不会自行写工程，导出窗口显示目标并可切换独立导出。清除地图关联只影响地图，不删除工程工具。

严格保留USER CODE：只有完整、校验通过的六文件目录可更新；无内容变化不改目录，更新前复查并保留备份。外部同名路径模块、修改的关联记录、路径越界/链接、非法XML拒绝覆盖。UVproj通过临时文件和原子替换更新，写前检查未被其他程序改动；UV写失败明确报告“文件已导出、登记待完成”，解除占用后可重试或由生成后工具恢复。

官方0.3.6生成后工具按确切SHA白名单可升级到独立037目录，原helper字节保留；外部或修改过的hook仍拒绝覆盖。第一次修改保留.ioc/.extSettings备份，工程关联记录采用自有格式。

验证由实际EXE的路径/工程场景、既有主机/地图/动作测试、CubeMX6.18.1原生生成与Keil6.22完整编译链接组成。实际验收工程会导出路径后删除登记，再Generate确认底盘4H、路径3C3H及包含目录恢复，路线手写区域和main.c保持原字节。无烧录或实车测试。

[发布说明](https://github.com/FoxMolika/Cubemx_RCS/releases/tag/v0.3.7) ｜ [代码](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.3.7)

### 0.3.6 · 项目改名说明

本版基于完整验收的RCS 0.3.5，在独立目录foxmolika_CubeMX_0.3.6发布，旧版不覆盖。

| 对外入口 | 新名称 |
|---|---|
| CubeMX软件包 | RCS.foxmolika 0.3.6 |
| 组件和页面 | Robotics / foxmolika；foxmolika页面启用Robotics foxmolika |
| 合并安装器 | foxmolika_CubeMX_Setup_0.3.6_win-x64.exe |
| 地图编辑器 | foxmolika_MapEditor.exe，版本0.2.2 |
| 工程关联工具 | foxmolika_ProjectSetup.exe |
| 自动头文件工具 | foxmolika_Tools_036/foxmolika_PostGenerate.exe |
| 默认安装目录 | %LOCALAPPDATA%\foxmolika_CubeMX_Suite_0.3.6 |

公开RCS_*函数、rcs_*.c/.h文件、RCS配置宏、.rcsmap.json格式及USER CODE机器标记保留，不因产品改名破坏已有调用和用户区合并。

这是新包身份，不是给RCS.Chassis同一版本悄悄替换内容。安装/卸载按RCS供应商和foxmolika包名查注册及仓库目录；旧RCS目录保留。安装器、地图窗口、程序文件、教程及元数据同步改名。

旧工程切换：备份→在CubeMX取消RCS组件，选择并启用foxmolika组件→仅清除旧RCS生成后工具（如有）→保存关闭.ioc→使用本版“关联工程…”→重新打开并Generate Code→Keil重载。工具拒绝旧RCS组件或两套同时启用，也拒绝覆盖已有生成后操作。

功能和算法延续0.3.5：独立底盘3C4H、路径3C3H、航点动作、保留USER CODE、CAN HAL依赖、每工程关联后自动登记4H。地图仍为独立窗口。此前与DeepSeek的风格比较保留在docs/与DeepSeek版本对比.md，指向改名前的0.3.5，属于历史证据；本版只是改名，不声称重新增加这些功能。

本次发布继续绑定最终二进制进行主机、地图/动作、工程关联、安装事务、CubeMX6.18.1原生生成与Keil6.22完整编译链接验收。无烧录或实车测试。

[发布说明](https://github.com/FoxMolika/Cubemx_RCS/releases/tag/v0.3.6) ｜ [代码](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.3.6)

### 0.3.5 · 固件风格统一与教程重写

本版在新目录中基于0.3.4继续，保留原目录及DeepSeek/Fix版本。

- 采纳DeepSeek六个固件文件的注释/排版方式，同时保留CAN未启用明确报错和原接口。
- 底盘7文件、路径6文件统一可读格式；配置头由离线生成和CubeMX共用前缀/约束模板，地图内嵌route/actions模板，用户不依赖外部格式化器。
- 风格检查补齐四空格缩进、词法处理、for括号与相邻函数文档；USER CODE按原样保留，新增17个风格/迁移场景。
- 地图编辑器升0.2.1，航点动作与0.2.0用户区兼容，动作C侧断言补充actions_required配置。
- 重写套件和.pack网页教程，明确“每工程主动关联一次→重开.ioc→Generate→Keil重载”；地图教程首屏给出独立编辑器按钮和安装路径。
- 保留0.3.4的工程关联、CAN HAL依赖、Keil4H生成后补全、可恢复安装事务、微分/BOFF/邮箱/编译期约束修复。它们不是0.3.5重新发明的功能。

对比结论和具体证据见 docs/与DeepSeek版本对比.md。硬件初始化仍需按接线配置；没有向main.c写底盘调用，也没有包含0.4内嵌绘图实验。

[发布说明](https://github.com/FoxMolika/Cubemx_RCS/releases/tag/v0.3.5) ｜ [代码](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.3.5)

### 0.3.4 · CAN HAL 依赖与 Keil 头文件自动化

## 0.3.4：CAN依赖和自动头文件补全

修复用户tester123工程没有配置CAN时出现的CAN_HandleTypeDef等编译错误。安装后点“关联工程…”选择已保存的.ioc：工具合并.extSettings以启用CAN HAL驱动，并在CubeMX的生成后操作中登记工程内RCS_PostGenerate.exe。每个工程只关联一次，后续Generate Code自动登记4个头文件，不需要手动运行脚本。

关联工具支持F407 HAL + Keil MDK，能将已有RCS组件版本切换到0.3.4；保留原.ioc和.extSettings备份，不覆盖已有的其他生成后操作。安装本身不会扫描或改写用户工程。实际硬件CAN1/CAN2引脚、波特率、滤波和中断仍需按接线配置；启用CAN HAL只保证驱动可编译，并不完成硬件初始化。

用户的0.3.3目录和test原工程保留。修复版示例和验收见dist/tester123_fixed_0.3.4.zip与docs/0.3.4修复说明.md。

缺失HAL_CAN_MODULE_ENABLED时输出明确配置错误，避免未知CAN类型的连锁报错。原0.3.3及test工程不修改。地图编辑器0.2.0及动作功能保留，本版不包含0.4嵌入实验。

[发布说明](https://github.com/FoxMolika/Cubemx_RCS/releases/tag/v0.3.4) ｜ [代码](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.3.4)

### 0.3.3 · 航点动作预留

在0.3.2合并修复及安装事务恢复的基础上，地图编辑器升级0.2.0。

- 每个航点支持启用、名称、超时、启动、完成条件、完成清理、取消清理和公共头文件。夹爪/舵机/升降提供注释模板，不绑定未知驱动。
- 输出独立路径3C3H，新增rcs_actions.c/h，使用rcs_route_init绑定动作，不修改main.c。
- 非阻塞状态机到点停稳后启动，等待时输出零速度，持续校验反馈/时间和漂移；停止、故障、超时调用一次取消清理。
- USER CODE区域按稳定航点ID保留；同时编辑冲突、删除手写动作、修改生成区域时拒绝覆盖，成功更新保留上一份目录备份。
- v1地图读取后迁移为v2；旧版四文件输出保留，需要另选新目录。
- 底盘固件延续已修复的微分冲击、硬件bus-off检测、发送中止等待、编译期配置约束和C++头文件兼容。包使用新版本避免同版本内容冲突；安装事务恢复逻辑沿用0.3.2。

验证入口：map_editor/tools/verify.py、tools/verify.py、suite/tools/test_transactions.py、suite/tools/verify.py和finish_tests.py。动作说明见map_editor/docs/航点动作说明.md。无实车/烧录验收，安装器未签名。

[发布说明](https://github.com/FoxMolika/Cubemx_RCS/releases/tag/v0.3.3) ｜ [代码](https://github.com/FoxMolika/Cubemx_RCS/tree/v0.3.3)

## 许可

自写模块与工具适用 MIT 许可，详见 [LICENSE.txt](LICENSE.txt)。ST/CMSIS 文件保留各自许可。
