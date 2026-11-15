"""Check current onboarding and relative links, no legacy text assumptions."""
from pathlib import Path
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
  for phrase in ['0.4.1','启动地图编辑器','foxmolika_MapEditor.exe','关联工程…','到达后动作','起步前动作','独立纠偏','量角器','CrashReports','ZIP','导出到关联工程','Application/foxmolikaRoute','3C3H','构建前']:
   assert phrase in text,(rel,phrase)
  for link in page.hrefs:
   if link.startswith('#'):assert link[1:] in page.ids,(rel,link)
   elif not re.match(r'https?://',link):assert (file.parent/link.split('#')[0]).is_file(),(rel,link)
 assert (ROOT/paths[0]).read_bytes()==(ROOT/paths[1]).read_bytes()
 result={'passed':True,'tutorials':{name:hashlib.sha256((ROOT/name).read_bytes()).hexdigest() for name in paths},'all_relative_links_resolve':True}
 (ROOT/'build/guide_validation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
 print('PASS 4 tutorials: install/open/associate, phases/protractor/crash ZIP, export/prebuild and local links')
if __name__=='__main__':main()
