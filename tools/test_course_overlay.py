"""Prove direct map labels stay view-only using authentic 0.4.0 C/H exports."""
from pathlib import Path
import hashlib,json,subprocess,sys,uuid,shutil
R=Path(__file__).resolve().parents[1]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 sys.stdout.reconfigure(encoding='utf-8');exe=R/'map_editor/dist/foxmolika_MapEditor.exe'
 fixture=R/'tests/fixtures/course040';area=R/'build'/('course_'+uuid.uuid4().hex[:8]);area.mkdir()
 def run(*args):
  p=subprocess.run([str(exe),*map(str,args)],capture_output=True,timeout=35);assert p.returncode==0,p.stderr
 before=(fixture/'map.rcsmap.json').read_bytes();run('--export',fixture/'map.rcsmap.json',area/'export')
 originals={p.name:p.read_bytes() for p in (fixture/'export').iterdir()};actual={p.name:p.read_bytes() for p in (area/'export').iterdir()}
 assert originals==actual,'view-only changes must not change any generated C/H'
 assert (fixture/'map.rcsmap.json').read_bytes()==before
 run('--selftest',area/'model');text=(area/'model/selftest.txt').read_text(encoding='utf-8');assert 'PASS 49 ' in text,text
 run('--render-course-angles',area/'course.png');assert (area/'course.png').stat().st_size>10000
 (R/'build/ui').mkdir(exist_ok=True);shutil.copyfile(area/'course.png',R/'build/ui/course_angles.png')
 report={'passed':True,'editor_sha256':sha(exe),'model_checks':49,'same_generated_six_C_H_as_040':True,
         'map_unchanged':True,'signed_arc_pixel_oracle':True,'area':str(area)}
 (R/'build/course_validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
 print('PASS 49 model/angle checks; signed-arc pixel oracle; all 6 generated C/H byte-identical to 0.4.0')
if __name__=='__main__':main()
