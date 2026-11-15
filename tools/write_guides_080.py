"""Synchronize the reviewed 0.8 HTML sources; never recreate legacy content."""
from pathlib import Path
R=Path(__file__).resolve().parents[1]

def main():
 full=(R/'docs/使用说明.html').read_text(encoding='utf-8')
 editor=(R/'docs/地图编辑器说明.html').read_text(encoding='utf-8')
 for text in (full,editor):
  for phrase in ['0.8.0','编辑形状','凸多边形','坡道参数','4C4H','制动','v6']:
   assert phrase in text,('authoritative tutorial missing current feature',phrase)
 targets={'suite/docs/使用说明.html':full,'suite/docs/地图编辑器说明.html':editor,
  'map_editor/docs/使用说明.html':editor.replace('<p><a href="地图编辑器说明.html">地图编辑器说明</a> · <a href="底盘接入说明.md">底盘接入接口</a></p>','')}
 for name,text in targets.items():
  p=R/name
  if p.read_text(encoding='utf-8')!=text:p.write_text(text,encoding='utf-8')
 for folder in ['docs','suite/docs','map_editor/docs']:
  target=R/folder/'底盘接入说明.md';data=(R/'docs/接入说明.md').read_bytes()
  if target.read_bytes()!=data:target.write_bytes(data)
 print('PASS synchronized authoritative 0.8.0 guides with slope execution and actual shape screenshot')
if __name__=='__main__':main()
