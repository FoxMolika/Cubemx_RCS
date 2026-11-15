"""Author synchronized standalone HTML tutorials for the 0.4 workflow."""
from pathlib import Path
import base64
R=Path(__file__).resolve().parents[1]
STYLE='body{max-width:1080px;margin:36px auto;padding:0 24px;color:#23344a;background:#f6f8fc;font:17px/1.8 "Microsoft YaHei",sans-serif}h1,h2{color:#135b77}section{background:white;padding:22px 28px;margin:20px 0;border-radius:12px}code,pre{background:#edf2f7;border-radius:5px;padding:3px 7px}pre{padding:14px;white-space:pre-wrap}table{border-collapse:collapse;width:100%}th,td{border:1px solid #cbd7e3;padding:9px;text-align:left}img{max-width:100%;border:1px solid #d6e0eb}a{color:#136c9b}.note{border-left:4px solid #2d8c98;padding-left:14px}'
def page(title,body):return '<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>'+title+'</title><style>'+STYLE+'</style><h1>'+title+'</h1>'+body+'</html>'

def main():
 body='''<p>2026-11-15 · STM32F407 HAL / CubeMX / Keil MDK · 独立 C/H 生成</p>
<section><h2>1. 安装与打开编辑器</h2><ol>
<li>保存并关闭 CubeMX。运行单文件 <code>foxmolika_CubeMX_Setup_0.5.2_win-x64.exe</code>，安装器自动寻找 CubeMX、导入内嵌软件包并核验注册，不需要手动导入 .pack，也不需要安装 Python。</li>
<li>目标电脑需要已有完整 STM32CubeMX；本次本机验收版本为 6.18.1。其他 CubeMX 版本没有自动视为已验收。</li>
<li>安装后点击安装器的<strong>启动地图编辑器</strong>。以后可运行安装目录的 <code>foxmolika_MapEditor.exe</code>。</li>
<li>默认安装目录为 <code>%LOCALAPPDATA%\\foxmolika_CubeMX_Suite_0.5.2</code>。安装器允许选择其他目录。编辑器是与 CubeMX 配套的独立窗口，不能在 CubeMX 画布里寻找地图入口。</li></ol>
<p class="note">0.3.7、旧安装目录与旧工程不会被本次发布目录覆盖。建议复制工程后再迁移，保留能够工作的固件。</p></section>
<section><h2>1.1 普通用户安装与“访问被拒绝”</h2>
<p>0.5.2修复了此前发布EXE继承Low完整性标签的问题。普通用户应以<strong>Medium、非管理员</strong>运行；当前账号自己的<code>AppData/Local</code>通常可写。Low级别程序会被Windows禁止写入普通用户数据目录，即使文件夹DACL允许该账号写入。</p>
<p>正常双击本版EXE即可。若外部目录重新给文件加了Low标签，安装器会明确提示；也提供<code>foxmolika_CubeMX_Installer_0.5.2.zip</code>，可在正常用户目录解压后运行其中的单个EXE。压缩包是兼容的分发方式，不需要手动导入.pack。</p>
<p>安装前实际检查工具目录、暂存父目录、所需CubeMX仓库与注册路径的写权限；导入工作目录和临时文件在工具自己的目录，不再写CubeMX程序目录。真正不可写时会显示完整路径，可以用“浏览…”更换工具目录；若是CubeMX仓库不可写，则需在CubeMX改为当前用户可写仓库，或在确有权限需求时处理该仓库权限。安装器不自动修改ACL或改动仓库位置。</p>
<p>错误报告默认保存到当前用户<code>%LOCALAPPDATA%/foxmolika/InstallerReports</code>，失败时依次尝试临时目录及安装器旁，并在弹窗中给出实际保存路径。报告记录异常、路径和实际Windows令牌状态，不上传。不同权限实例冲突会提示先关闭已有安装器。</p></section>
<section><h2>2. CubeMX 选择组件，并主动关联工程</h2><ol>
<li>打开 F407 的 .ioc，Select Components 中选择 <code>RCS.foxmolika 0.5.2</code>，在 foxmolika 页启用 Robotics/foxmolika 模式并配置底盘参数。</li>
<li>保存并关闭 .ioc。点击安装器或地图编辑器顶部绿色栏的<strong>关联工程…</strong>，主动选择该 .ioc。仅安装软件不会自动关联任何工程。</li>
<li>重新打开 .ioc，Generate Code，然后在 Keil 重载工程。生成后工具自动补齐底盘头文件。</li>
<li>CAN 引脚、时钟、位时序、滤波、接收中断、启动和任务调用仍须按硬件完成。关联仅保证 HAL 依赖及文件登记，不等于已经接好电机。</li></ol>
<p>关联记录在工程内，工具目录为 <code>foxmolika_Tools_052</code>。官方 0.3.6/0.3.7/0.4.0/0.4.1/0.5.0 生成后工具经哈希检查后可升级；旧工具保留。未知自定义生成后命令会保留并提示合并。</p></section>
<section><h2>3. 航点流程：平移、动作、纠偏分开</h2>
<pre>平移到点 → 停稳 → 到达后动作 → 独立纠偏 → 起步前动作 → 离开验收 → 下一点</pre>
<p>选择一个点，点<strong>航点流程…</strong>。窗口同时显示<strong>① 到达航点后</strong>与<strong>③ 离开航点前</strong>两个独立动作框，中间是<strong>② 位置纠错</strong>。两个框各有添加、编辑、上移、下移和删除按钮，不再通过下拉框切换。主窗口表格也分成“到达后动作”和“离开前动作”两列，双击任一列即可打开流程。</p>
<p>“到达后动作”和“起步前动作”保留原有执行顺序与数据；旋转不再依靠重叠位点表达，普通行驶只平移。取消整个流程窗口会放弃本次修改。</p>
<table><tr><th>动作类型</th><th>填写内容</th><th>执行含义</th></tr>
<tr><td>绝对旋转</td><td>地图目标角度、方向、限速、角加速度、容差、停稳与漂移上限</td><td>XY 命令为零，仅转向</td></tr>
<tr><td>相对旋转</td><td>相对动作开始时车头的角度</td><td>开始时锁存目标，支持累计转角</td></tr>
<tr><td>等待</td><td>秒数、超时</td><td>保持底盘零命令，控制与反馈继续运行</td></tr>
<tr><td>自定义机构</td><td>公共头文件、启动、完成条件、完成清理、取消清理</td><td>调用夹爪/舵机/升降已有函数，必须非阻塞</td></tr></table>
<p>纠偏设置包括关闭/位置/位置与朝向/只验收、到达半径、精调速度、超时、次数、停稳和漂移限制。平移精调与朝向精调串行；已完成的机构动作不会因纠偏重试而重放。旋转成功会更新后续目标朝向。</p>
<p>终点默认不执行起步前组；需要在终点执行时，勾选“终点也执行起步前动作”。通过后结束任务，不再向外发起一段行驶。</p></section>
<section><h2>4. 地图直接显示夹角，量角器继续保留</h2>
<p><strong>默认开启“显示夹角”</strong>：每个有下一段路径的航点，直接显示计划车头与出发连线之间的角度弧和“顺时针/逆时针 + 度数”，不用打开量角器，也不用逐点点击。</p>
<p>橙色虚线代表<strong>该点动作执行后的计划出发朝向</strong>；蓝线代表航行轨迹；紫色/青色弧线及文字是两者之间的最短转头参考角。例如 P0=(600,600)、P1=(4200,1200)，P0 出发朝向为 0°时，直接显示“P0→P1 逆时针 9.46°”。</p>
<p>拖动位点、修改初始朝向或旋转动作会自动刷新；缩放不改变角度。已对齐显示 0.00°，终点不显示不存在的出发角，重合段不冒充 0°。选中点加粗标注，标签尽力避让节点与路径；密集时可以放大，或用工具栏“显示夹角”隐藏/恢复。</p>
<p class="note">这些是计划夹角，不是实测姿态、路径拐角或动作累计转角。全向底盘可以侧向平移，非零夹角不代表路径错误；显示标注不会自动添加旋转动作，也不改变地图参数或生成的运动代码。</p>

<p>点击<strong>量角器</strong>，选择 A、B 位点。可查看 A→B 对 +X、+Y 的有向角，以及与水平/竖直网格线的夹角。地图 +X 向右、+Y 向上，逆时针为正。</p>
<p>选择动作组和插入位置，参考朝向按此前计划动作推导；也可手动输入。面板显示从参考朝向面向连线所需的相对转角。例如连线 45°、车头 90°，需要顺时针 45°。全向车的行进方向与车头方向不必一致。</p>
<p>生成旋转动作时选择到达后或起步前。保留位点引用时，拖动位点会重新计算方向；固定角度则保持数值。删除引用点会使校验失败。两点重合没有连线方向，不能生成“朝向连线”的动作。</p></section>
<section><h2>5. 保存地图、导出 C/H，再编译</h2><ol>
<li>保存地图。旧 v1/v2 地图会迁移为 v3 并另存，原地图保留；旧机构动作映射到起步前，角度变化映射为旋转动作，停留映射为等待。</li>
<li>点<strong>导出 C / H</strong>，选择<strong>导出到关联工程</strong>。输出到 <code>Application/foxmolikaRoute</code>，自动登记路线的 3C3H 和包含目录。</li>
<li>在 Keil 重载工程并 Rebuild。底盘 3C4H 加路线 3C3H 仍是独立源码，不向 main.c 插入业务。</li>
<li>保存地图后再次修改，必须重新导出。关联导出会登记 Keil 构建前版本检查；地图改了没导出、路线配置源码被更改或地图路径失效时会阻止构建并给出提示。</li></ol>
<p>状态栏显示已保存/未保存和当前版本已导出/未导出。生成的 <code>rcs_route_revision</code> 供核对，不代表调试器已经加载了同一份固件。请使用同一输出目录配套的 AXF 与 HEX。</p>
<p>重新导出会保留 USER CODE。不要修改生成区域来绕开校验。独立导出没有关联工程时，需要自行登记文件；构建前检查针对关联工程配置。</p></section>
<section><h2>6. 崩溃日志：本地 ZIP 与恢复副本</h2>
<p>不可恢复异常时编辑器停止业务操作，尽力记录诊断并生成本地 ZIP，报错窗口显示完整路径，可复制路径、打开文件夹或关闭。<strong>不上传、不发送邮件。</strong></p>
<p>默认位置：<code>%LOCALAPPDATA%\\foxmolika\\CrashReports</code>。ZIP 包含异常堆栈、版本、运行环境与有限操作记录，不附地图、用户代码、工程、Keil 编译日志或许可证。恢复地图单独保存在本地，原文件不覆盖；下次启动可选择恢复。</p>
<p>打包失败时显示原始日志的实际路径；落盘全部失败则明确提示。强制结束或断电不能保证当场报告，下次启动可补收未正常结束的会话记录。自动快照之后的最后修改可能丢失。</p></section>
<section><h2>7. 立体地形：平台、坡道、桥上桥下</h2>
<p>主窗口上方新增米色地形栏。点击<strong>立体示例</strong>可查看“地面→直坡道→桥面”的示例；点击<strong>立体地形…</strong>编辑当前地图。首次进入并应用后保存为v4格式；旧v1/v2迁移与v3平地地图继续支持，原文件请另存保留。v4不能交给旧编辑器使用。</p>
<h3>0.5.2：直接拖动平台和坡道</h3>
<ol><li>在地形窗口切到<strong>俯视图</strong>，添加平台/坡道，或从左侧列表选中已有表面。</li>
<li>保持<strong>拖动表面</strong>勾选，在色块空白处按住鼠标左键拖动。橙色边框表示选中，画布与左侧属性实时显示X/Y。点到蓝色航点时只选择该点；请从色块空白处拖。</li>
<li>默认<strong>网格吸附</strong>；按住<strong>Shift</strong>临时取消吸附，也可取消勾选。整个移动包络受场地边界限制，边界优先于吸附，不能把元素拖出场地。</li>
<li>默认<strong>携带本面航点/障碍</strong>，同SurfaceId的航点和障碍一起平移，坡道上的局部位置和规划高度保持；其他层、相邻连接平台/坡道不跟随。关闭后只移动表面，已有实体保持世界坐标，可能需要重新调整归属/位置。此开关只作用于拖动；左侧X/Y数值精调仍按原有方式只修改表面几何。</li>
<li>重叠时先从左侧列表选中目标，再拖色块空白处，选中的表面优先。地面固定不移动。三维视图拖动用于旋转视角；侧视图不移动地形。</li>
<li>松开鼠标完成一次移动并立即检查连接和路线。移动会暂时断开坡道接边，可继续调整其他表面；检查未通过时“应用地形”会提示修正，不会自动重连或改高度。</li>
<li>拖动中<strong>Esc / 右键</strong>还原；窗口变化或鼠标捕获丢失也还原。窗口内<strong>撤销 / 重做</strong>按一次拖动整体恢复，支持Ctrl+Z / Ctrl+Y；属性输入框保留自身编辑快捷键。取消整个地形窗口会放弃本次全部编辑。</li>
<li>完成后点<strong>应用地形</strong>，回主窗口再<strong>保存</strong>。仍可用左侧X/Y数值做精调。</li></ol>
<ol><li>在“表面”页添加水平平台或沿X/沿Y直坡道，设置位置、长宽、起端/末端高度、限速和向下厚度。坡道沿坐标增大方向从起端变为末端高度；交换两端高度可表示下坡。地面固定Z=0。</li>
<li>在“连接”页明确声明可通行的两个表面。平台与坡道须在等高边接合；桥上桥下即使XY相同，也不会自动连接。每段路线最多跨一次表面接合；连续经过多个表面时，应增加航点。</li>
<li>在“航点归属”和“障碍归属”页指定所在表面。高度Z由表面与XY实时推导，不单独输入，避免相互矛盾。删除仍被航点、障碍或连接引用的表面会被拒绝，需先调整引用。</li>
<li>使用<strong>俯视图 / 路线侧视图 / 三维预览</strong>检查路线，点击节点联动选择。侧视横轴为累计水平距离，标注分段空间路程与表面坡度；纵轴独立缩放。三维视图拖动旋转视角，滚轮缩放。预览显示上表面，净空计算包含配置的向下厚度。</li>
<li>回到主窗口，选择<strong>当前编辑表面</strong>后再添加、拖动位点或画障碍；仅当前表面参与鼠标选取，防止点到桥上/桥下的另一层。从表格选点会切换到其所属表面。</li></ol>
<p>检查包含表面越界、失效引用、未声明连接、高度断层、路线空隙、车体包络、坡度上限、表面限速和桥下净空。净空取决于车体总高度、余量和桥面厚度；请在“车体/能力参数”中填写真实包络与限制。默认参数仅为示例，不代表车辆已具备爬坡能力。坡道点需关闭普通纠错，不放停车/机构动作；这些动作应位于平地端点。</p>
<p class="note"><strong>0.5.2扩展立体地图操作，不含坡道运动控制。</strong>立体路线可以保存、编辑和查看几何预览；涉及坡道或高台的路线会明确拒绝导出C/H与原有平地动态预览，不会悄悄忽略Z。全程地面路线在通过净空等检查后，继续使用原有平地固件。Z是规划值，没有实测高度闭环、自动寻路、IMU融合或真实车身姿态仿真。表面仅支持轴对齐矩形；台阶、升降平台和任意曲面待后续模块。</p>
<p>地图窗口内编辑的是副本，点“应用地形”才提交到主窗口；取消不改变原地图。应用后仍须保存地图，可在主窗口撤销/重做。主窗口可保存未连完的立体草稿；重新打开时提示问题并允许继续修正，非有限数值、零尺寸等不可安全绘制的数据仍会拒绝。打开草稿不代表通过导出检查。</p></section>
<section><h2>8. 接入与验证边界</h2>
<p>任务侧使用 <code>rcs_route_init/reset/update/start</code> 或示例 <code>robot_route_task.c/.h</code>，持续调用底盘控制周期。必须提供真实时间与全部在用轮的新鲜编码器增量；生成器不自动配置编码器硬件、解绕原始角度或替代 CAN 板级接入。</p>
<p>轮速 PID 初值仍需调参。编码器纠偏修正的是估计坐标，不能自动识别全部打滑误差；实车旋转中心还取决于轮速跟随、安装几何与接地状态。软件测试、ARM 编译和实车验收分开记录。</p>
<p><a href="地图编辑器说明.html">地图编辑器说明</a> · <a href="底盘接入说明.md">底盘接入接口</a></p></section>'''
 for name,label in [('phases.png','航点双动作框实际界面'),('terrain0.png','俯视图直接拖动平台和坡道'),('terrain2.png','立体地形三维预览实际界面')]:
  image=R/'build/ui'/name
  if not image.exists():image=R/'map_editor/docs'/name
  if image.exists():body+='<section><h2>'+label+'</h2><img alt="'+label+'" src="data:image/png;base64,'+base64.b64encode(image.read_bytes()).decode()+'"></section>'
 full=page('foxmolika 0.5.2 安装与使用教程',body)
 for name in ['docs/使用说明.html','suite/docs/使用说明.html']:(R/name).write_text(full,encoding='utf-8')
 image=R/'map_editor/docs/editor_ui.png'
 preview='<p>界面以已安装版本为准。</p>'
 if image.exists():preview='<img alt="foxmolika0.4地图编辑器实际界面" src="data:image/png;base64,'+base64.b64encode(image.read_bytes()).decode()+'">'
 editor=page('foxmolika 0.5.2 地图、动作与量角器',preview+body)
 (R/'map_editor/docs/使用说明.html').write_text(editor,encoding='utf-8')
 (R/'docs/地图编辑器说明.html').write_text(editor,encoding='utf-8')
 (R/'suite/docs/地图编辑器说明.html').write_text(editor,encoding='utf-8')
 for folder in ['docs','suite/docs','map_editor/docs']:
  (R/folder/'底盘接入说明.md').write_bytes((R/'docs/接入说明.md').read_bytes())
 # Embedded standalone editor guide contains the complete workflow; remove links
 # whose targets are intentionally not separate files in its small installation.
 standalone=editor.replace('<p><a href="地图编辑器说明.html">地图编辑器说明</a> · <a href="底盘接入说明.md">底盘接入接口</a></p>','')
 (R/'map_editor/docs/使用说明.html').write_text(standalone,encoding='utf-8')
 print('Wrote synchronized 0.5.2 Chinese tutorials')
if __name__=='__main__':main()
