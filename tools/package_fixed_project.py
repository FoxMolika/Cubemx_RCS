from pathlib import Path
from xml.etree import ElementTree as E
import json,hashlib,shutil,zipfile
R=Path(__file__).resolve().parents[1]
README='本工程由你提供的tester123副本修复，原test工程未改动。\n直接打开 MDK-ARM/tester123.uvprojx，选择ARM Compiler6.22并Rebuild。已实测0错误0警告。\n需要重新生成时，先安装RCS.foxmolika0.8.0，再打开tester123.ioc并Generate Code；本副本已完成工程关联，会自动补头文件。保留foxmolika_Tools_080目录。\nCAN HAL驱动已进入工程，但CAN引脚/波特率/滤波/接收中断仍需按接线配置；main.c没有新增底盘调用，未烧录/实车测试。\n'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 r=json.loads((R/'build/project_integration_validation.json').read_text(encoding='utf-8'));assert r['keil']=='pass: ARM Compiler 6.22, 0 errors, 0 warnings'
 src=Path(r['project_directory']);dest=R/'repaired_project/tester123';assert src.resolve().is_relative_to(R.resolve()) and dest.resolve().is_relative_to(R.resolve())
 if dest.exists():
  for p in dest.rglob('*'):
   if p.is_file():
    if p.relative_to(dest).as_posix()=='README_先读.txt':
     assert p.read_text(encoding='utf-8')==README,'Delivery README was edited; retained';continue
    original=src/p.relative_to(dest);assert original.is_file() and p.read_bytes()==original.read_bytes(),'Existing delivery project differs; retained: '+str(p)
 else:dest.mkdir(parents=True)
 for p in src.rglob('*'):
  if not p.is_file():continue
  rel=p.relative_to(src);name=p.name.lower()
  if rel.parts[:2]==('MDK-ARM','tester123') or 'MXTmpFiles' in rel.parts:continue
  if p.suffix.lower() in ['.o','.d','.crf','.axf','.hex','.map','.dep','.lnp','.htm','.bak','.lst','.tra']:continue
  if '.uvguix.' in name or name.endswith(('.rcs-before-080','.rcs-backup')) or name=='headers-result.txt':continue
  target=dest/rel;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(p,target)
 assert sha(dest/'MDK-ARM/tester123.uvprojx')==r['keil_project_sha256'];assert sha(dest/'Core/Src/main.c')==r['main_sha256']
 for f in E.parse(dest/'MDK-ARM/tester123.uvprojx').findall('.//File'):
  path=f.findtext('FilePath')
  if path is None and f.findtext('FileName') is None:continue # inert CubeMX placeholder
  assert path and (dest/'MDK-ARM'/path).is_file(),path
 assert sha(dest/'foxmolika_Tools_080/foxmolika_PostGenerate.exe')==r['post_helper_sha256']
 (dest/'README_先读.txt').write_text(README,encoding='utf-8')
 out=R/'dist/tester123_fixed_0.8.0.zip'
 with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED) as z:
  for p in sorted(dest.rglob('*')):
   if p.is_file():z.write(p,'tester123/'+p.relative_to(dest).as_posix())
 (R/'dist/tester123_验证记录.json').write_text(json.dumps(r,ensure_ascii=False,indent=2),encoding='utf-8')
 print(out);print('SHA256',sha(out));print('project',dest)
if __name__=='__main__':main()
