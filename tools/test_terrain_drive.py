"""Real8-file terrain export, strict C simulation, geometry/UI and file integration."""
from pathlib import Path
import copy,hashlib,json,re,subprocess,sys,uuid
R=Path(__file__).resolve().parents[1]
def main():
 sys.stdout.reconfigure(encoding='utf-8');area=R/'build'/('terrain_drive_'+uuid.uuid4().hex[:8]);area.mkdir();exe=R/'map_editor/dist/foxmolika_MapEditor.exe'
 def run(args):
  p=subprocess.run(list(map(str,args)),capture_output=True,timeout=90);assert p.returncode==0,(args,p.returncode,p.stdout.decode(errors='replace'),p.stderr.decode(errors='replace'));return p.stdout.decode('utf-8',errors='replace').strip()
 for flag in ['--terrain-drive-selftest','--shape-geometry-selftest','--shape-ui-selftest']:run([exe,flag,area])
 reports={name:(area/name).read_text().strip() for name in ['terrain-drive-selftest.txt','shape-geometry-selftest.txt','shape-ui-selftest.txt']}
 results=[]
 for layout in range(3):
  folder=area/('layout'+str(layout));run([exe,'--export',area/('terrain-layout'+str(layout)+'.json'),folder]);assert len(list(folder.iterdir()))==8
  run([sys.executable,R/'tools/style_check.py',folder]);out=area/('sim'+str(layout)+'.exe')
  run(['gcc','-std=c99','-O2','-Wall','-Wextra','-Werror','-pedantic','-I',folder,'-I',R/'firmware',R/'map_editor/tests/test_generated_terrain.c',folder/'rcs_route.c',folder/'rcs_nav.c',folder/'rcs_actions.c',folder/'rcs_terrain.c',R/'firmware/rcs_chassis.c','-lm','-o',out]);results.append(run([out]))
 folder=area/'multi';run([exe,'--export',area/'terrain-multi.json',folder]);out=area/'multi.exe'
 run(['gcc','-std=c99','-O2','-Wall','-Wextra','-Werror','-pedantic','-I',folder,'-I',R/'firmware',R/'map_editor/tests/test_generated_terrain.c',folder/'rcs_route.c',folder/'rcs_nav.c',folder/'rcs_actions.c',folder/'rcs_terrain.c',R/'firmware/rcs_chassis.c','-lm','-o',out]);multi=run([out])
 variants={};base=json.loads((area/'terrain-layout0.json').read_text(encoding='utf-8-sig'))
 reverse=copy.deepcopy(base);reverse['Points'].reverse();variants['reverse']=reverse
 rotated=copy.deepcopy(base);rotated['Settings'].update(Width=5000,Height=10000)
 for s in rotated['Terrain']['Surfaces']:
  x,y,w,h=s['X'],s['Y'],s['Width'],s['Depth'];s.update(X=5000-y-h,Y=x,Width=h,Depth=w,Kind=2 if s['Kind']==1 else 0)
 for p in rotated['Points']:p.update(X=5000-p['Y'],Y=p['X'],Yaw=90)
 variants['rotated90']=rotated;variant_reports={}
 for name,doc in variants.items():
  source=area/(name+'.json');source.write_text(json.dumps(doc,ensure_ascii=False),encoding='utf-8');folder=area/name;run([exe,'--export',source,folder]);out=area/(name+'.exe')
  run(['gcc','-std=c99','-O2','-Wall','-Wextra','-Werror','-pedantic','-I',folder,'-I',R/'firmware',R/'map_editor/tests/test_generated_terrain.c',folder/'rcs_route.c',folder/'rcs_nav.c',folder/'rcs_actions.c',folder/'rcs_terrain.c',R/'firmware/rcs_chassis.c','-lm','-o',out]);variant_reports[name]=run([out])
 out=area/'terrain-runtime.exe';run(['gcc','-std=c99','-O2','-Wall','-Wextra','-Werror','-pedantic','-I',R/'map_editor/firmware',R/'map_editor/tests/test_terrain_runtime.c',R/'map_editor/firmware/rcs_terrain.c','-lm','-o',out]);runtime=run([out])
 folder=area/'layout0';out=area/'terrain-task.exe'
 run(['gcc','-std=c99','-O2','-Wall','-Wextra','-Werror','-pedantic','-I',R/'map_editor/tests/task_stub','-I',folder,'-I',R/'map_editor/examples','-I',R/'firmware',R/'map_editor/tests/test_terrain_task.c',R/'map_editor/examples/robot_route_task.c',R/'map_editor/examples/terrain_brake_port.c',folder/'rcs_route.c',folder/'rcs_nav.c',folder/'rcs_actions.c',folder/'rcs_terrain.c',R/'firmware/rcs_chassis.c','-lm','-o',out]);task=run([out])
 for flag,name in [('--render-shapes','shape-editing.png')]:
  target=R/'build/ui'/name;target.parent.mkdir(exist_ok=True);run([exe,flag,target]);assert target.stat().st_size>12000
 report={'passed':True,'editor_sha256':hashlib.sha256(exe.read_bytes()).hexdigest(),'model_UI_geometry_reports':reports,'actual_export_simulations':results,'multi_leg_simulation':multi,'direction_simulations':variant_reports,'runtime_report':runtime,'task_adapter_report':task,'generated_files':8,'hardware_test':False,'area':str(area)}
 (R/'build/terrain_drive_validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print('\n'.join(reports.values()));print('\n'.join(results));print(multi);print(variant_reports);print(runtime);print(task)
if __name__=='__main__':main()
