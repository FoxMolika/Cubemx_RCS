"""Package already-built binaries; never launches or rebuilds GUI applications."""
from pathlib import Path
import hashlib,json,shutil,sys,zipfile
sys.stdout.reconfigure(encoding='utf-8')
ROOT=Path(__file__).resolve().parents[1]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    dist=ROOT/'dist';shutil.copyfile(ROOT.parent/'LICENSE.txt',ROOT/'LICENSE.txt')
    files={
      'RCS_MapEditor.exe':dist/'RCS_MapEditor.exe','使用说明.html':dist/'使用说明.html','LICENSE.txt':ROOT/'LICENSE.txt',
      'README.md':ROOT/'README.md','验证记录.md':ROOT/'docs/验证记录.md','examples/示例路线.rcsmap.json':ROOT/'examples/示例路线.rcsmap.json',
      'examples/robot_route_task.c':ROOT/'examples/robot_route_task.c','examples/robot_route_task.h':ROOT/'examples/robot_route_task.h',
      'sdk/rcs_chassis.c':ROOT.parent/'firmware/rcs_chassis.c','sdk/rcs_chassis.h':ROOT.parent/'firmware/rcs_chassis.h'}
    portable=dist/'RCS_MapEditor_Portable_0.2.0_win-x64.zip'
    with zipfile.ZipFile(portable,'w',zipfile.ZIP_DEFLATED) as z:
        for name,p in files.items():z.write(p,'RCS_MapEditor/'+name)
    source=dist/'RCS_MapEditor_Source_0.2.0.zip'
    with zipfile.ZipFile(source,'w',zipfile.ZIP_DEFLATED) as z:
        for folder in ['src','firmware','tools','tests','examples','docs']:
            for p in sorted((ROOT/folder).rglob('*')):
                if p.is_file() and '__pycache__' not in p.parts:z.write(p,'RCS_MapEditor_Source/'+p.relative_to(ROOT).as_posix())
        for name in ['README.md','LICENSE.txt','installer.cs']:z.write(ROOT/name,'RCS_MapEditor_Source/'+name)
    for src,dest in [('dist/map-editor-error.log','build/early_editor_test_error.log'),('dist/setup-error.log','build/expected_installer_refusal.log')]:
        p=ROOT/src
        if p.is_file():shutil.copyfile(p,ROOT/dest);p.unlink()
    artifacts=[dist/'RCS_MapEditor.exe',dist/'RCS_MapEditor_Setup_0.2.0_win-x64.exe',portable,source,dist/'使用说明.html',dist/'示例路线.rcsmap.json']
    (dist/'SHA256SUMS.txt').write_text(''.join(sha(p)+'  '+p.name+'\n' for p in artifacts),encoding='utf-8')
    for p in artifacts:print(p.name,p.stat().st_size)
    (ROOT/'build/delivery.json').write_text(json.dumps({'artifacts':{p.name:{'bytes':p.stat().st_size,'sha256':sha(p)} for p in artifacts},'ui_control':'stopped after physical Escape; no further input','hardware_test':False},ensure_ascii=False,indent=2),encoding='utf-8')
if __name__=='__main__':main()
