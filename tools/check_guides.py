"""Keep shipped onboarding instructions aligned with the actual entry points."""
from pathlib import Path
from html.parser import HTMLParser
import re
ROOT=Path(__file__).resolve().parents[1]

class Page(HTMLParser):
 def __init__(self):super().__init__();self.ids=set();self.hrefs=[]
 def handle_starttag(self,tag,attrs):
  a=dict(attrs)
  if 'id' in a:self.ids.add(a['id'])
  if tag=='a' and 'href' in a:self.hrefs.append(a['href'])

def main():
 paths=['docs/使用说明.html','suite/docs/使用说明.html','map_editor/docs/使用说明.html']
 for rel in paths:
  text=(ROOT/rel).read_text(encoding='utf-8');p=Page();p.feed(text)
  for phrase in ['关联工程…','选择 .ioc 并关联 / 升级','启动地图编辑器','foxmolika_MapEditor.exe',
                 '%LOCALAPPDATA%\\foxmolika_CubeMX_Suite_0.3.7','0.3.0','foxmolika_Tools_037/headers-result.txt']:
   assert phrase in text,(rel,phrase)
  for link in p.hrefs:
   if link.startswith('#'):assert link[1:] in p.ids,(rel,link)
   elif not re.match(r'https?://',link):assert (ROOT/rel).parent.joinpath(link.split('#')[0]).is_file(),(rel,link)
  assert '编辑器 0.2.0' not in text,rel
  for phrase in ['Application/foxmolikaRoute','导出到关联工程','3C3H']:assert phrase in text,(rel,phrase)
  for obsolete in ['关联不负责添加地图','自动补全的范围是底盘 4 个','CubeMX 重新生成不会替你添加这三个']:assert obsolete not in text,(rel,obsolete)
 assert (ROOT/paths[0]).read_bytes()==(ROOT/paths[1]).read_bytes()
 print('PASS: three tutorials contain current association/editor entry points; local links and anchors resolve')

if __name__=='__main__':main()
