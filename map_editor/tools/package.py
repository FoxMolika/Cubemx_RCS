"""Package already-built binaries; never launches or rebuilds GUI applications."""
from pathlib import Path
import hashlib,json,shutil,sys,zipfile
sys.stdout.reconfigure(encoding='utf-8')
ROOT=Path(__file__).resolve().parents[1]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    dist=ROOT/'dist';shutil.copyfile(ROOT.parent/'LICENSE.txt',ROOT/'LICENSE.txt')
    files={
      'foxmolika_MapEditor.exe':dist/'foxmolika_MapEditor.exe','使用说明.html':dist/'使用说明.html','LICENSE.txt':ROOT/'LICENSE.txt',
      'README.md':ROOT/'README.md','验证记录.md':ROOT/'docs/验证记录.md','examples/示例路线.rcsmap.json':ROOT/'examples/示例路线.rcsmap.json',
      'examples/robot_route_task.c':ROOT/'examples/robot_route_task.c','examples/robot_route_task.h':ROOT/'examples/robot_route_task.h',
      'sdk/rcs_chassis.c':ROOT.parent/'firmware/rcs_chassis.c','sdk/rcs_chassis.h':ROOT.parent/'firmware/rcs_chassis.h'}
    portable=dist/'foxmolika_MapEditor_Portable_0.3.0_win-x64.zip'
    with zipfile.ZipFile(portable,'w',zipfile.ZIP_DEFLATED) as z:
        for name,p in files.items():z.write(p,'foxmolika_MapEditor/'+name)
    source=dist/'foxmolika_MapEditor_Source_0.3.0.zip'
    # Association code is shared with the suite. Ship the complete verified source,
    # rather than a map-only tree missing the helper and root build dependencies.
    verified_source=ROOT.parent/'dist/foxmolika_CubeMX_Source_0.3.7.zip'
    if not verified_source.is_file():raise RuntimeError('Run the full suite release first; map source now includes shared project integration.')
    shutil.copyfile(verified_source,source)
    for src,dest in [('dist/map-editor-error.log','build/early_editor_test_error.log'),('dist/setup-error.log','build/expected_installer_refusal.log')]:
        p=ROOT/src
        if p.is_file():shutil.copyfile(p,ROOT/dest);p.unlink()
    artifacts=[dist/'foxmolika_MapEditor.exe',dist/'foxmolika_MapEditor_Setup_0.3.0_win-x64.exe',portable,source,dist/'使用说明.html',dist/'示例路线.rcsmap.json']
    (dist/'SHA256SUMS.txt').write_text(''.join(sha(p)+'  '+p.name+'\n' for p in artifacts),encoding='utf-8')
    for p in artifacts:print(p.name,p.stat().st_size)
    (ROOT/'build/delivery.json').write_text(json.dumps({'artifacts':{p.name:{'bytes':p.stat().st_size,'sha256':sha(p)} for p in artifacts},'ui_validation':'programmatic minimum-window render inspected; no new interactive UI acceptance','hardware_test':False},ensure_ascii=False,indent=2),encoding='utf-8')
if __name__=='__main__':main()
