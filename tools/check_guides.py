"""Check current onboarding and relative links, no legacy text assumptions."""
from pathlib import Path
from version import VERSION
from html.parser import HTMLParser
import hashlib,json,re
ROOT=Path(__file__).resolve().parents[1]
class Page(HTMLParser):
 def __init__(self):super().__init__();self.ids=set();self.hrefs=[]
 def handle_starttag(self,tag,attrs):
  a=dict(attrs)
  if 'id' in a:self.ids.add(a['id'])
  if tag=='a' and 'href' in a:self.hrefs.append(a['href'])
def main():
 paths=['docs/使用说明.html','suite/docs/使用说明.html','map_editor/docs/使用说明.html','docs/地图编辑器说明.html']
 for rel in paths:
  file=ROOT/rel;text=file.read_text(encoding='utf-8');page=Page();page.feed(text)
  for phrase in ['拖动表面','携带本面航点/障碍','Shift','右键','撤销','三维视图拖动用于旋转视角']:assert phrase in text,(rel,phrase)
  for phrase in ['Low完整性标签','Medium','InstallerReports','访问被拒绝']:assert phrase in text,(rel,phrase)
  for phrase in ['连续跨平台','入段表面链','保持起点层','不必在每个坡道或交界处放航点','不是自动绕路','纠错模板','定位硬件','空模板','独立观测','v5','编辑形状','凸多边形','坡道参数','4C4H','制动','v6',VERSION,'启动地图编辑器','foxmolika_MapEditor.exe','关联工程…','到达后动作','起步前动作','独立纠偏','量角器','CrashReports','ZIP','导出到关联工程','Application/foxmolikaRoute','3C3H','构建前','到达航点后','离开航点前','主窗口一体编辑','所属表面','向下厚度','拒绝导出','v4','保存草稿','当前层','＋航点','未接合']:
   assert phrase in text,(rel,phrase)
  for link in page.hrefs:
   if link.startswith('#'):assert link[1:] in page.ids,(rel,link)
   elif not re.match(r'https?://',link):assert (file.parent/link.split('#')[0]).is_file(),(rel,link)
 assert (ROOT/paths[0]).read_bytes()==(ROOT/paths[1]).read_bytes()
 beta=ROOT/'docs/0.8.4使用说明.html';beta_text=beta.read_text(encoding='utf-8')
 for phrase in ['0.8.4','硬件管理','新建模板','App_Init','App_Task','应用与状态机','App_Init','App_Task','Machism_Task','19','21',
                'RCS_CAN_PORT_DEFINE_HAL_CALLBACK','独立观测设备','RX0','main.c','模板快照']:
  assert phrase in beta_text,('beta tutorial',phrase)
 assert 'script src=' not in beta_text and 'src="http' not in beta_text
 result={'passed':True,'tutorials':{name:hashlib.sha256((ROOT/name).read_bytes()).hexdigest() for name in paths},'all_relative_links_resolve':True}
 (ROOT/'build/guide_validation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
 print('PASS 4 tutorials: install/open/associate, phases/protractor/crash ZIP, export/prebuild and local links')
 print('PASS beta self-contained tutorial: application/state machine, fixed main, CAN prerequisites, migration and delivery')
if __name__=='__main__':main()
