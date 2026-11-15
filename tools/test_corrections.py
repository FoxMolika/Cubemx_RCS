"""Model/UI, real generated callback integration and C fault/feedback tests."""
from pathlib import Path
import hashlib,json,re,subprocess,sys,uuid
R=Path(__file__).resolve().parents[1]
def main():
 sys.stdout.reconfigure(encoding='utf-8');area=R/'build'/('corrections_'+uuid.uuid4().hex[:8]);area.mkdir();exe=R/'map_editor/dist/foxmolika_MapEditor.exe'
 def run(args):
  p=subprocess.run(list(map(str,args)),capture_output=True,timeout=60);assert p.returncode==0,(args,p.returncode,p.stdout.decode(errors='replace'),p.stderr.decode(errors='replace'));return p.stdout.decode('utf-8',errors='replace').strip()
 run([exe,'--correction-selftest',area]);model=(area/'correction-selftest.txt').read_text().strip();checks=int(re.search(r'PASS (\d+)',model).group(1));assert checks>=38
 folder=area/'generated';run([exe,'--export',area/'correction-fixture.json',folder]);assert len(list(folder.iterdir()))==6
 run([sys.executable,R/'tools/style_check.py',folder]);out=area/'generated-test.exe'
 args=['gcc','-std=c99','-O2','-Wall','-Wextra','-Werror','-pedantic','-I',folder,'-I',R/'firmware',R/'map_editor/tests/test_generated_correction.c',folder/'rcs_route.c',folder/'rcs_nav.c',folder/'rcs_actions.c',R/'firmware/rcs_chassis.c','-lm','-o',out];run(args);generated=run([out])
 out=area/'runtime.exe';run(['gcc','-std=c99','-O2','-Wall','-Wextra','-Werror','-pedantic','-I',R/'map_editor/firmware','-I',R/'firmware',R/'map_editor/tests/test_corrections.c',R/'map_editor/firmware/rcs_nav.c',R/'firmware/rcs_chassis.c','-lm','-o',out]);runtime=run([out]);assert 'independent correction assertions' in runtime
 cpp=area/'header.cpp';cpp.write_text('#include "rcs_nav.h"\nint main(){rcs_navigator n{};return rcs_nav_bind_corrections(&n,nullptr,0,nullptr,nullptr);}',encoding='ascii')
 for source,name in [(R/'map_editor/firmware/rcs_nav.c','nav.o'),(R/'firmware/rcs_chassis.c','chassis.o')]:run(['gcc','-std=c99','-I',R/'map_editor/firmware','-I',R/'firmware','-c',source,'-o',area/name])
 run(['g++','-std=c++17','-Wall','-Wextra','-Werror','-pedantic','-I',R/'map_editor/firmware','-I',R/'firmware',cpp,area/'nav.o',area/'chassis.o','-lm','-o',area/'cpp.exe']);run([area/'cpp.exe'])
 image=R/'build/ui/correction-templates.png';image.parent.mkdir(exist_ok=True);run([exe,'--render-corrections',image]);assert image.stat().st_size>12000
 run([exe,'--render-hardware',R/'build/ui/hardware-profile.png'])
 report={'passed':True,'editor_sha256':hashlib.sha256(exe.read_bytes()).hexdigest(),'model_checks':checks,'new_API_Cpp_link':True,'model_report':model,'runtime_report':runtime,'generated_observer_integration':generated,'area':str(area),'hardware_test':False}
 (R/'build/correction_validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print(model);print(runtime);print(generated)
if __name__=='__main__':main()
