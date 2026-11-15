"""Exercise the actual terrain editor, blocking boundary and dual-action UI."""
from pathlib import Path
import hashlib,json,re,subprocess,sys,uuid
R=Path(__file__).resolve().parents[1]
def main():
 sys.stdout.reconfigure(encoding='utf-8');exe=R/'map_editor/dist/foxmolika_MapEditor.exe'
 area=R/'build'/('terrain_'+uuid.uuid4().hex[:8]);area.mkdir();ui=R/'build/ui';ui.mkdir(exist_ok=True)
 def run(*args,expected=0):
  p=subprocess.run([str(exe),*map(str,args)],capture_output=True,timeout=40)
  assert p.returncode==expected,(args,p.returncode,p.stderr.decode('utf-8',errors='replace'))
 run('--terrain-selftest',area)
 checks=int(re.search(r'PASS (\d+)',(area/'terrain-selftest.txt').read_text()).group(1));assert checks>=95
 drag_checks=int(re.search(r'PASS (\d+)',(area/'drag-selftest.txt').read_text()).group(1));assert drag_checks>=38
 demo=area/'terrain.rcsmap.json';run('--terrain-demo',demo);before=demo.read_bytes()
 # Task waypoints on ramps remain unsupported; reject before output mutations.
 destination=area/'owned-export';destination.mkdir();sentinel=destination/'user.txt';sentinel.write_bytes(b'preserve')
 run('--export',demo,destination,expected=2);assert list(destination.iterdir())==[sentinel] and sentinel.read_bytes()==b'preserve'
 assert demo.read_bytes()==before
 for view in range(3):run('--render-terrain',view,ui/('terrain'+str(view)+'.png'))
 run('--render-phases',ui/'phases.png');run('--render-editor',ui/'editor.png');run('--render-terrain-main',ui/'terrain-main.png')
 assert all(p.stat().st_size>12000 for p in ui.glob('*.png'))
 (R/'build/terrain_validation.json').write_text(json.dumps({'passed':True,'editor_sha256':hashlib.sha256(exe.read_bytes()).hexdigest(),'checks':checks,'actual_drag_checks':drag_checks,'unsupported_ramp_stop_export_refused_before_mutation':True,'terrain_map_unchanged':True,'actual_WinForms_renderings':6,'area':str(area)},indent=2),encoding='utf-8')
 print('PASS',checks,'terrain/action UI checks; unsupported ramp-stop export refused without file changes; 6 actual UI renders')
if __name__=='__main__':main()
