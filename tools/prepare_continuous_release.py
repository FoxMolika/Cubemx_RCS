"""One-time 0.6.1 source and documentation integration."""
from pathlib import Path
import base64,hashlib,re
R=Path(__file__).resolve().parents[1]
def edit(name,fn):
 p=R/name;p.write_text(fn(p.read_text(encoding='utf-8')),encoding='utf-8')
def main():
 old=R.parent/'foxmolika_CubeMX_0.6.0/build/foxmolika_PostGenerate.exe'
 data=old.read_bytes();digest=hashlib.sha256(data).hexdigest()
 (R/'tests/fixtures/legacy_post_060.base64.txt').write_text(base64.b64encode(data).decode(),encoding='ascii')
 edit('keil_headers/ProjectSetup.cs',lambda s:s.replace(' const string Legacy052Hash=',f' const string Legacy060Hash="{digest}";\n const string Legacy052Hash=').replace('?Legacy052Hash:null;','?Legacy052Hash:\n   normalized.Equals("foxmolika_Tools_060/foxmolika_PostGenerate.exe",StringComparison.OrdinalIgnoreCase)?Legacy060Hash:null;'))
 def upgrade(s):
  a=s.index(" legacy,lioc,luv,lmap=fixture('upgrade052')");b=s.index(" report=",a)
  return s[:b]+s[a:b].replace('052','060').replace('0.5.2','0.6.0')+s[b:]
 edit('tools/test_revision_guard.py',upgrade)
 section='''<h3>连续跨平台：只在任务位置放航点</h3>
<ol><li>使用<strong>＋平台 / ＋坡道</strong>绘制地形，拖到实际位置；坡道末端与平台接合边必须等高，不能有悬空缺口。</li>
<li>在左侧<strong>连接</strong>页声明地面↔上坡道↔平台↔下坡道↔地面。连接表示可通行关系，仍需真实接边；未接合的连接会报错。</li>
<li>只在起点、终点或真正需要任务的位置放航点，并选择其<strong>所属表面</strong>。两点间的直线可以连续跨越多个已连接的平台和坡道，<strong>不必在每个坡道或交界处放航点</strong>。</li>
<li>选中目的航点，右下属性的<strong>入段表面链</strong>显示上一点到本点的完整表面顺序及最小限速。<strong>路线侧视 / 三维预览</strong>显示沿途真实规划高度，包含端点同在地面时中间的高台。</li>
<li>点击<strong>立体示例</strong>可打开仅有P0、P1的连续上坡、平台、下坡示例。安装目录的<code>examples/连续通行示例.rcsmap.json</code>也可直接打开。</li>
<li>交界处是内部几何分段，<strong>不会新增任务航点，不会在那里停车、纠错或执行动作</strong>。任务仍按到达后动作→独立纠偏→离开前动作执行；有停车动作/纠错的航点请放在水平平台。</li></ol>
<p>默认<strong>入段地形策略＝沿连接表面</strong>。若有明确的桥下路线，可选<strong>保持起点层</strong>（两端须同层）；仍检查桥板厚度、车体高度和净空，不能穿过低矮坡道。多条可行表面链会报层次歧义，不按列表顺序猜测。</p>
<p>连续通行只沿当前航段的直线检查连接，<strong>不是自动绕路</strong>。若道路在平台上拐弯，请在水平平台加转向航点。每个中间表面的限速、坡度、车体包络、障碍和净空都会检查；中途断链不会悄悄改走地面。</p>
<h3>可选插点与草稿</h3>
<p>要细调路线时先选择前一航点，再选当前层、点<strong>＋航点</strong>，新点插在所选点后；没有选中点时追加到末尾。坡道点默认无动作且关闭纠错。点击<strong>检查</strong>查看问题；没连接完成也可以保存草稿。</p>
'''
 for name in ['docs/使用说明.html','suite/docs/使用说明.html','map_editor/docs/使用说明.html','docs/地图编辑器说明.html','suite/docs/地图编辑器说明.html']:
  def guide(s):
   a=s.index('<h3>在地面与平台之间补坡道过渡点</h3>');b=s.index('<h3>拖动与精调</h3>',a)
   return (s[:a]+section+s[b:]).replace('0.5.1/0.5.2 生成后','0.5.1/0.5.2/0.6.0 生成后')
  edit(name,guide)
 edit('map_editor/tools/build.py',lambda s:s.replace("    payload={", "    run([exe,'--continuous-demo',ROOT/'examples/连续通行示例.rcsmap.json'])\n    payload={").replace('"examples/robot_route_task.c":', '"examples/连续通行示例.rcsmap.json":ROOT/\'examples/连续通行示例.rcsmap.json\',"examples/robot_route_task.c":'))
 edit('suite/tools/build.py',lambda s:s.replace("  'examples/立体地图示例.rcsmap.json':", "  'examples/连续通行示例.rcsmap.json':PROJECT/'map_editor/examples/连续通行示例.rcsmap.json',\n  'examples/立体地图示例.rcsmap.json':"))
 edit('tools/test_install_permissions.py',lambda s:s.replace(" assert root_files==", " invoke(dest/'foxmolika_MapEditor.exe',['--continuous-selftest',area/'installed-continuous-tests']);assert 'PASS ' in (area/'installed-continuous-tests/continuous-selftest.txt').read_text()\n assert root_files==").replace("'installed_workspace_checks':41,", "'installed_workspace_checks':41,'installed_continuous_report':(area/'installed-continuous-tests/continuous-selftest.txt').read_text().strip(),"))
 edit('suite/tools/verify.py',lambda s:s.replace(" workspace_report=", " invoke(dest/'foxmolika_MapEditor.exe',['--continuous-selftest',area/'installed-continuous-tests'],env)\n continuous_report=(area/'installed-continuous-tests/continuous-selftest.txt').read_text(encoding='utf-8').strip()\n workspace_report=").replace("'installed_workspace_report':workspace_report,", "'installed_continuous_report':continuous_report,'installed_workspace_report':workspace_report,"))
 edit('tools/accept_061.py',lambda s:s.replace("['tools/check_guides.py'],", "['tools/check_guides.py'],['tools/test_continuous.py'],"))
 edit('tools/check_guides.py',lambda s:s.replace("'0.6.1','启动", "'连续跨平台','入段表面链','保持起点层','不必在每个坡道或交界处放航点','不是自动绕路','0.6.1','启动"))
 edit('tools/release_061.py',lambda s:s.replace(" workspace=read(", " continuous=read('build/continuous_validation.json');assert continuous['passed'] and continuous['editor_sha256']==sha(R/'map_editor/dist/foxmolika_MapEditor.exe')\n workspace=read(").replace(" assert integration['installer_sha256']", " assert accepted['installed_continuous_report']==permissions['installed_continuous_report']==continuous['report']\n assert integration['installer_sha256']").replace(' # Hash actual embedded resources', " previous060=read('build/original_060_SHA256.json');root060=R.parent/'foxmolika_CubeMX_0.6.0'\n for name,digest in previous060.items():assert sha(root060/name)==digest,('060 changed',name)\n # Hash actual embedded resources").replace(" shutil.copyfile(R/'build/ui/course_angles.png'", " shutil.copyfile(R/'build/ui/continuous1.png',dist/'连续跨平台侧视图.png')\n shutil.copyfile(R/'build/ui/continuous2.png',dist/'连续跨平台三维图.png')\n shutil.copyfile(R/'map_editor/examples/连续通行示例.rcsmap.json',dist/'连续通行示例.rcsmap.json')\n shutil.copyfile(R/'build/ui/course_angles.png'").replace("'features':[", "'continuous_surface_checks':continuous['checks'],'original_060_files_unchanged':len(previous060),'features':['continuous connected-surface traversal without forced task waypoints',").replace("'delegated_review':'two parallel DeepSeek text reviews: shared state/draft architecture and original modal deadlock regression design. Codex implemented and verified actual control events, rendered UI, ordinary-user installation and native generation'", "'delegated_review':'parallel DeepSeek design/edge-case reviews plus bounded topology review; Codex implemented, checked actual geometry/state/UI and ran installed/native regression'").replace("'ui_review':'actual unified", "'ui_review':'two-endpoint continuous route profile and 3D inspected; actual unified"))
 (R/'更新说明_0.6.1.md').write_text('''# 0.6.1 连续跨平台

修复旧版每段最多跨两个表面的限制。航段沿直线自动解析显式连接的地面、坡道、平台链，不再要求在坡道放任务航点。交界只做内部几何分段，不执行动作、纠错或停车。

目的航点属性新增入段地形策略和入段表面链。默认沿连接表面；明确桥下路线可保持起点层。中间表面的限速、坡度、障碍、净空和车体包络全部检查。侧视和三维预览显示完整起伏；同在地面的两端也不会掩盖中间高台。

“立体示例”改为两个地面航点连续上下平台，可在安装目录 examples/连续通行示例.rcsmap.json 打开。旧v3/v4仍可读，默认策略不增加旧JSON字段；旧版目录和用户工程不修改。只升级已通过哈希检查的官方0.6.0生成后工具。

范围：仍是立体地图几何规划，尚未生成实车坡道控制。不是自动寻路；拐弯需在水平平台加转向航点。平地C/H生成和现有硬件驱动不变。安装器是内嵌pack的单文件EXE，保留普通用户Medium安装修复。
''',encoding='utf-8')
if __name__=='__main__':main()
