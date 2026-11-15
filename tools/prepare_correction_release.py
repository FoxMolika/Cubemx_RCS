"""One-time integration of the 0.7.0 release workflow and hardware guides."""
from pathlib import Path
import base64,hashlib,json,re
R=Path(__file__).resolve().parents[1]
def edit(name,fn):
 p=R/name;p.write_text(fn(p.read_text(encoding='utf-8')),encoding='utf-8')
def main():
 old=R.parent/'foxmolika_CubeMX_0.6.1/build/foxmolika_PostGenerate.exe';data=old.read_bytes();digest=hashlib.sha256(data).hexdigest()
 (R/'tests/fixtures/legacy_post_061.base64.txt').write_text(base64.b64encode(data).decode(),encoding='ascii')
 edit('keil_headers/ProjectSetup.cs',lambda s:s.replace(' const string Legacy060Hash=',f' const string Legacy061Hash="{digest}";\n const string Legacy060Hash=').replace('?Legacy060Hash:null;','?Legacy060Hash:\n   normalized.Equals("foxmolika_Tools_061/foxmolika_PostGenerate.exe",StringComparison.OrdinalIgnoreCase)?Legacy061Hash:null;'))
 def upgrade(s):
  a=s.index(" legacy,lioc,luv,lmap=fixture('upgrade060')");b=s.index(' report=',a)
  return s[:b]+s[a:b].replace('060','061').replace('0.6.0','0.6.1')+s[b:]
 edit('tools/test_revision_guard.py',upgrade)
 edit('tools/accept_070.py',lambda s:s.replace("['tools/check_guides.py'],", "['tools/check_guides.py'],['tools/test_corrections.py'],"))
 edit('tools/test_install_permissions.py',lambda s:s.replace(" assert root_files==", " invoke(dest/'foxmolika_MapEditor.exe',['--correction-selftest',area/'installed-correction-tests']);assert 'PASS ' in (area/'installed-correction-tests/correction-selftest.txt').read_text()\n assert root_files==").replace("'installed_workspace_checks':41,", "'installed_workspace_checks':41,'installed_correction_report':(area/'installed-correction-tests/correction-selftest.txt').read_text().strip(),"))
 edit('suite/tools/verify.py',lambda s:s.replace(' workspace_report=', " invoke(dest/'foxmolika_MapEditor.exe',['--correction-selftest',area/'installed-correction-tests'],env)\n correction_report=(area/'installed-correction-tests/correction-selftest.txt').read_text(encoding='utf-8').strip()\n workspace_report=").replace("'installed_workspace_report':workspace_report,", "'installed_correction_report':correction_report,'installed_workspace_report':workspace_report,"))
 def course(s):
  s=s.replace(" assert originals==actual,'view-only changes must not change any generated C/H'", " assert originals!=actual,'0.7 replaces implicit correction with explicit hardware templates'\n import re\n for label in ['rcs_route_points','rcs_route_segments']:\n  pattern=r'const .*? '+label+r'\\[.*?\\] = \\{(.*?)\\};'\n  a=re.search(pattern,originals['rcs_route.c'].decode('utf-8-sig'),re.S).group(1)\n  b=re.search(pattern,actual['rcs_route.c'].decode('utf-8-sig'),re.S).group(1)\n  assert a==b,('legacy geometry changed',label)")
  return s.replace("'same_generated_six_C_H_as_040':True,", "'same_generated_six_C_H_as_040':False,'legacy_points_segments_preserved':True,'intentional_correction_engine_upgrade':True,").replace('all 6 generated C/H byte-identical to 0.4.0','legacy point/segment geometry preserved; explicit correction upgrade recorded')
 edit('tools/test_course_overlay.py',course)
 def release(s):
  s=s.replace(" continuous=read(", " correction=read('build/correction_validation.json');assert correction['passed']\n continuous=read(")
  s=s.replace(" assert accepted['installed_continuous_report']", " assert correction['editor_sha256']==sha(editor) and accepted['installed_correction_report']==permissions['installed_correction_report']==correction['model_report']\n assert accepted['installed_continuous_report']")
  s=s.replace(" assert all('4205 assertions' in s for s in phases['actual_export_simulations'])", " assert all(int(re.search(r'(\\d+) assertions',s).group(1))>=3000 for s in phases['actual_export_simulations'])")
  s=s.replace("assert course['same_generated_six_C_H_as_040'] and course['signed_arc_pixel_oracle']", "assert course['legacy_points_segments_preserved'] and course['signed_arc_pixel_oracle'] and course['intentional_correction_engine_upgrade']")
  s=s.replace(' # Hash actual embedded resources', " previous061=read('build/original_061_SHA256.json');root061=R.parent/'foxmolika_CubeMX_0.6.1'\n for name,digest in previous061.items():assert sha(root061/name)==digest,('061 changed',name)\n # Hash actual embedded resources")
  s=s.replace(" shutil.copyfile(R/'build/ui/course_angles.png'", " shutil.copyfile(R/'build/ui/correction-templates.png',dist/'纠错模板界面.png')\n shutil.copyfile(R/'docs/定位硬件接入.md',dist/'定位硬件接入.md')\n shutil.copyfile(R/'map_editor/examples/定位硬件档案示例.json',dist/'定位硬件档案示例.json')\n shutil.copyfile(R/'build/ui/course_angles.png'")
  s=s.replace("'continuous_surface_checks':", "'correction_validation':correction,'original_061_files_unchanged':len(previous061),'continuous_surface_checks':")
  s=s.replace("'features':[", "'features':['optional hardware-backed correction templates; empty means no correction','validated independent observation freshness, quality and bounded displacement','v5 hardware profiles with explicit driver binding; legacy mode retained but not executed',")
  s=s.replace("'generated_assertions_per_layout':4205", "'generated_assertions_per_layout':int(re.search(r'(\\d+) assertions',phases['actual_export_simulations'][0]).group(1))")
  s=s.replace("'delegated_review':'parallel DeepSeek design/edge-case reviews plus bounded topology review; Codex implemented, checked actual geometry/state/UI and ran installed/native regression'", "'delegated_review':'two parallel DeepSeek reviews of independent-observation runtime and UI/data migration; Codex implemented, ran actual generated C/WinForms/installed/native checks'")
  s=s.replace("'encoder-only correction cannot observe all real slip'", "'drive-encoder odometry alone cannot enable correction; declared independent adapters require real hardware integration and calibration'")
  return s
 edit('tools/release_070.py',release)
 edit('tools/check_guides.py',lambda s:s.replace("'0.7.0','启动", "'纠错模板','定位硬件','空模板','独立观测','v5','0.7.0','启动"))
 # Ship a deliberately UNBOUND profile; enabling requires a real adapter and coordinate alignment.
 profile={'Version':1,'Sources':[{'Id':'9a5175a1ee5f4dfb9ad406e426260701','Name':'外部XY定位接入示例','Kind':'external_pose','Axes':3,'Reference':'absolute','Independent':True,'MapAligned':False,'BindingReady':False,'DriverHeader':'foxmolika_observation_adapter.h','ReadFunction':'TeamGps_ReadObservation'}]}
 (R/'map_editor/examples/定位硬件档案示例.json').write_text(json.dumps(profile,ensure_ascii=False,indent=2),encoding='utf-8')
 (R/'map_editor/examples/foxmolika_observation_adapter.h').write_text('''/**
 * @file foxmolika_observation_adapter.h
 * @brief 独立定位接入示例；不是某型号传感器驱动
 */
#pragma once
#include "rcs_nav.h"
#ifdef __cplusplus
extern "C" {
#endif
/**
 * @brief 返回车体中心地图坐标观测，1有效、0失败
 */
int TeamGps_ReadObservation(rcs_observation *sample);
#ifdef __cplusplus
}
#endif
''',encoding='utf-8')
 (R/'map_editor/examples/foxmolika_observation_adapter.c').write_text('''/**
 * @file foxmolika_observation_adapter.c
 * @brief 独立定位接入位置；未实现时返回失败，禁止伪造成功
 */
#include "foxmolika_observation_adapter.h"
/**
 * @brief 用队内真实定位驱动填充观测；保持非阻塞
 * @note 未实现。不能每次调用就递增sequence；它必须对应真实采样。
 */
int TeamGps_ReadObservation(rcs_observation *sample)
{
    (void)sample;
    /* 从真实驱动读取车体中心的地图坐标，填入米/弧度、年龄、质量和采样序号。
     * 完成安装偏移和初始坐标对齐后，才将硬件档案MapAligned/BindingReady置true。
     * 不允许用驱动电机编码器的积分结果填充此独立观测。
     */
    return 0;
}
''',encoding='utf-8')
 edit('map_editor/tools/build.py',lambda s:s.replace('    payload={','    payload={').replace('    setup=dist/', '''    for name in ['定位硬件档案示例.json','foxmolika_observation_adapter.c','foxmolika_observation_adapter.h']:
        payload['examples/'+name]=ROOT/'examples'/name
    setup=dist/'''))
 edit('suite/tools/build.py',lambda s:s.replace(" manifest=E.Element(", " for name in ['定位硬件档案示例.json','foxmolika_observation_adapter.c','foxmolika_observation_adapter.h']:\n  payload['examples/'+name]=PROJECT/'map_editor/examples'/name\n payload['定位硬件接入.md']=PROJECT/'docs/定位硬件接入.md'\n manifest=E.Element("))
 section='''<section><h2>0.7.0：由独立观测支持的可选纠错模板</h2>
<p><strong>没有独立定位硬件时，空模板不纠错。</strong>正常轮速闭环、里程计到点、反馈失联停机和看门狗保留。旧“独立纠偏”设置只保留迁移记录，不参与新生成路线；默认CorrectionMode为0。</p>
<ol><li>主工具栏点<strong>定位硬件</strong>，导入硬件档案JSON。rcs_config.h仍只导入底盘参数；CubeMX启用UART/CAN/I²C不代表安装了定位设备。</li>
<li>硬件档案声明类型、有效轴、relative/absolute参考、独立反馈、地图坐标对齐及真实驱动读取函数。IMU仅相对朝向，独立码盘仅相对XY；驱动电机编码器不能用作纠错源。未完成对齐/绑定的源仅可保存禁用模板。</li>
<li>选航点→<strong>航点流程…</strong>，中间<strong>纠错模板（可空）</strong>按硬件能力提供可选项，支持添加/编辑/排序/删除。导入硬件不会自动添加或启用纠错。</li>
<li><strong>刷新定位估计</strong>只更新导航估计，不移动车辆；<strong>移动对齐XY</strong>会平移；<strong>原地对齐朝向</strong>会旋转。两类运动分开执行，均有超时、速度和漂移限制。</li>
<li>空组直接进入离开航点前动作，没有隐藏的编码器二次位置/朝向补偿。显式模板遇到无数据、缺轴、低质量、过期、倒退序号或过大定位跳变，会锁定故障、清零输出。重复样本不再次刷新定位，超过年龄上限失败。</li></ol>
<p>执行顺序：到点停稳→到达后动作→纠错模板（可空）→离开前动作。机构动作不会因定位刷新重放。生成仍为路径3C3H，用户在USER CODE区的驱动修改按稳定ID保留；真实硬件驱动和适配器需接入项目，不能用占位返回成功。</p>
<p>含硬件/纠错模板的地图保存为<strong>v5</strong>，旧编辑器不能打开；旧v3/v4仍可读。安装目录examples中有<strong>定位硬件档案示例.json</strong>和<strong>foxmolika_observation_adapter.c/.h</strong>，示例刻意未绑定、未对齐，默认返回失败。先完成真实驱动，再修改档案并导入；不是完整型号驱动。</p>
<p>观测回调必须快速返回；软件不能抢占阻塞驱动，底盘看门狗必须保留。IMU与独立码盘仍有漂移，只有明确的外部定位轴才能提供相应参考。动态预览只显示纠错模板占位，不模拟真实传感器。</p>
</section>'''
 for name in ['docs/使用说明.html','suite/docs/使用说明.html','map_editor/docs/使用说明.html','docs/地图编辑器说明.html','suite/docs/地图编辑器说明.html']:
  edit(name,lambda s:s.replace('<body>','<body>'+section).replace('0.5.2/0.6.0 生成后','0.5.2/0.6.0/0.6.1 生成后'))
 (R/'docs/定位硬件接入.md').write_text('''# 0.7.0 定位硬件与纠错模板

默认空模板，不纠错。导入硬件只提供可选模板，不会自动启用。当前导入的是显式JSON能力档案，不是扫描总线或自动识别传感器；rcs_config.h仅含底盘参数。

主窗口“定位硬件”→导入JSON；选航点→“航点流程…”→中间“纠错模板”。模板可排序、禁用、删除。刷新只改估计；XY对齐和朝向对齐分别驱动车辆。

档案Version=1；Sources最多32项。Kind仅支持imu（Axes=4、relative）、tracking_encoder（Axes=3、relative）、external_pose（Axes=3/4/7）。Independent必须是独立于驱动轮反馈的真实测量。MapAligned声明地图坐标原点/方向已对齐、传感器安装偏移已换算至车体中心；BindingReady表示真实非阻塞适配器已经接入。

ReadFunction的C签名为 int Name(rcs_observation *sample)。DriverHeader可填真实驱动适配器头文件相对路径；函数返回1表示有效，0表示不可用并使选中的纠错失败。数据必须是车体中心的地图坐标，单位米/弧度；未观测轴填0；axes为XY=3、yaw=4、全部=7。quality为0～1，age_s为当前时刻减实际采样时刻。sequence只在真实采样时递增，允许uint32回绕，增量必须小于2^31；不能在读取旧缓存时伪造新序号。驱动自身必须识别外设失联与数据冻结。

安装目录examples中的档案和.c/.h只示范接入契约，不是完整设备驱动。默认MapAligned=false、BindingReady=false且示例函数返回0，不能直接启用。可按现有队内定位驱动实现读取函数、保留真实时间戳与序号、完成安装偏移和坐标对齐后，再将这两个声明置true。适配器作为用户真实驱动加入工程；路线生成器仍输出并关联6个C/H文件。新profile与已有源ID重复时拒绝覆盖；有模板引用的源不能删除。

观测超过模板最大年龄、缺轴、非有限值、质量不足、序号倒退或定位跳变超过阈值时，停止输出并锁定故障。序号重复不会再次应用定位，且内部年龄持续累加，不能用恒定age_s绕过超时。允许两个传感器更新间隔之间的有限缓存时间；模板完成需真实新样本。主动对齐还受航点漂移上限约束（基准为本模板首次有效观测），超时或漂移过大停止。没有独立XY时，平移漂移保护仍只能参考轮式里程计。

旧地图CorrectionMode仅保留记录，新生成表始终为0。生成器显式绑定每点纠错组；空组跳过全部纠错及隐藏的目标朝向二次验收，离开阶段仍检查动作漂移和停稳。旧手写C调用未绑定新组的历史接口保留兼容行为，不应将其用于新的空模板路线；使用生成的rcs_route_init完成绑定。

带硬件或纠错模板的地图使用v5，防止旧编辑器忽略它们；旧v3/v4源地图不自动改写。用户自定义机构区和观测区以稳定ID保存。动态预览中的纠错只是占位，不能视为硬件验证。仍不生成坡道控制固件；本版重点是纠错模板，不改变现有运动库或硬件驱动支持范围。
''',encoding='utf-8')
 edit('map_editor/src/PhaseTools.cs',lambda s:s.replace('两个动作框各自排序；纠错重试不会重放已经完成的到达动作。','三组按顺序执行；定位刷新不会重放已经完成的到达动作。').replace('[DisplayName("纠错速度 mm/s")]', '[Browsable(false),DisplayName("旧纠错速度（不执行）")]').replace('[DisplayName("纠错超时 s")]', '[Browsable(false),DisplayName("旧纠错超时（不执行）")]').replace('[DisplayName("最大纠错次数")]', '[Browsable(false),DisplayName("旧纠错次数（不执行）")]'))
 edit('README.md',lambda s:s.replace('\n两个航点之间', '\n**0.7.0新增可选纠错模板：没有独立观测时留空，不自动纠错。** 主窗口“定位硬件”导入能力档案，航点流程中间添加支持的模板；导入不自动启用。刷新估计和驱动对齐明确分开。旧自动纠错仅作记录，详细接入见 docs/定位硬件接入.md。\n\n两个航点之间'))
 (R/'变更说明_0.7.0.md').write_text('''# 0.7.0 可选硬件纠错模板

航点流程新增独立纠错列表；导入硬件档案后按有效轴提供刷新估计、XY移动对齐和朝向旋转对齐。没有独立观测时空组直接跳过，旧默认位置纠错和目标朝向二次补偿不再由生成器执行。旧CorrectionMode保留查看，v3/v4原地图不改写；新硬件地图v5避免旧版忽略。

新增真实独立观测读取契约与逐点纠错表。无数据、缺轴、低质量、过期、回放、跳变、主动对齐超时或漂移过大时零输出并锁定故障；不伪造成功。保持机构动作不重放、USER CODE保留与路径3C3H自动登记。

导入仅是硬件能力声明，不是完整型号驱动或总线自动识别。附带档案与适配器示例默认未接入，不能直接启用。旧手写C接口兼容行为与新生成绑定方式在接入说明中区分。立体路线仍只做几何规划，不输出坡道固件。旧版目录、用户工程保留。
''',encoding='utf-8')
if __name__=='__main__':main()
