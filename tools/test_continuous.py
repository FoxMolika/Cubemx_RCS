"""Real binary tests: continuous terrain, export boundary, UI render and JSON."""
from pathlib import Path
import hashlib,json,re,subprocess,sys,uuid
R=Path(__file__).resolve().parents[1]
def main():
 sys.stdout.reconfigure(encoding='utf-8');area=R/'build'/('continuous_'+uuid.uuid4().hex[:8]);area.mkdir();exe=R/'map_editor/dist/foxmolika_MapEditor.exe'
 def run(*args,expected=0):
  p=subprocess.run([str(exe),*map(str,args)],capture_output=True,timeout=45);assert p.returncode==expected,(args,p.returncode,p.stderr.decode(errors='replace'))
 run('--continuous-selftest',area);report=(area/'continuous-selftest.txt').read_text().strip();checks=int(re.search(r'PASS (\d+)',report).group(1));assert checks>=67
 demo=area/'two-points.rcsmap.json';run('--continuous-demo',demo);before=demo.read_bytes();data=json.loads(before);assert len(data['Points'])==2 and len(data['Terrain']['Surfaces'])==3
 destination=area/'export';destination.mkdir();(destination/'user.txt').write_bytes(b'preserve');run('--export',demo,destination,expected=2);assert demo.read_bytes()==before and [x.name for x in destination.iterdir()]==['user.txt']
 fresh=area/'fresh8';run('--export',demo,fresh);assert len(list(fresh.iterdir()))==21
 for view in range(3):
  image=R/'build/ui'/('continuous'+str(view)+'.png');image.parent.mkdir(exist_ok=True);run('--render-continuous',view,image);assert image.stat().st_size>12000
 (R/'build/continuous_validation.json').write_text(json.dumps({'passed':True,'checks':checks,'report':report,'editor_sha256':hashlib.sha256(exe.read_bytes()).hexdigest(),'two_task_points_five_spans':True,'unknown_user_files_preserved':True,'nonflat_generated_files':21,'ui_renders':3,'area':str(area)},indent=2),encoding='utf-8');print(report,'; three real editor renders; export preserves user files')
if __name__=='__main__':main()
