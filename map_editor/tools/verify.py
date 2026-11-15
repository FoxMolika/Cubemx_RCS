from pathlib import Path
import json,subprocess,sys,uuid,hashlib
sys.stdout.reconfigure(encoding='utf-8')
ROOT=Path(__file__).resolve().parents[1]
def run(args):
    p=subprocess.run([str(x) for x in args],cwd=ROOT,capture_output=True,text=True,encoding='utf-8',errors='replace')
    if p.returncode:raise RuntimeError(f'{args[0]} exit {p.returncode}\n{p.stdout}\n{p.stderr}')
    return p.stdout
def main():
    if '--no-build' not in sys.argv:run([sys.executable,ROOT/'tools/build.py'])
    area=ROOT/'build'/('验收 空间_'+uuid.uuid4().hex[:8]);area.mkdir(parents=True)
    exe=ROOT/'dist/RCS_MapEditor.exe'
    run([exe,'--selftest',area]);print((area/'editor-test-report.txt').read_text(encoding='utf-8').strip())
    results=[]
    for layout in range(3):
        folder=area/f'layout{layout}';out=folder/'nav_tests.exe'
        args=['gcc','-std=c99','-O2','-Wall','-Wextra','-Werror','-pedantic','-I'+str(folder),'-I'+str(ROOT.parent/'firmware'),ROOT/'tests/test_nav.c',folder/'rcs_nav.c',folder/'rcs_route.c',ROOT.parent/'firmware/rcs_chassis.c','-lm','-o',out]
        args.insert(args.index(ROOT.parent/'firmware/rcs_chassis.c'),folder/'rcs_actions.c')
        run([sys.executable,ROOT.parent/'tools/style_check.py',folder])
        run(args);result=run([out]);print(result.strip());results.append(result.strip())
    run([exe,'--render',ROOT/'build/map_preview.png'])
    run([exe,'--render-actions',ROOT/'build/actions_preview.png'])
    folder=area/'layout0';task=area/'task_test.exe'
    run(['gcc','-std=c99','-O2','-Wall','-Wextra','-Werror','-pedantic','-I'+str(ROOT/'tests'),'-I'+str(ROOT/'examples'),'-I'+str(folder),'-I'+str(ROOT.parent/'firmware'),ROOT/'tests/test_task.c',ROOT/'examples/robot_route_task.c',folder/'rcs_nav.c',folder/'rcs_route.c',folder/'rcs_actions.c',ROOT.parent/'firmware/rcs_chassis.c','-lm','-o',task])
    task_result=run([task]);print(task_result.strip())
    folder=area/'generated_actions';action=area/'action_test.exe'
    run([sys.executable,ROOT.parent/'tools/style_check.py',folder])
    run(['gcc','-std=c99','-O2','-Wall','-Wextra','-Werror','-pedantic','-I'+str(folder),'-I'+str(ROOT.parent/'firmware'),ROOT/'tests/test_actions.c',folder/'rcs_nav.c',folder/'rcs_route.c',folder/'rcs_actions.c',ROOT.parent/'firmware/rcs_chassis.c','-lm','-o',action])
    action_result=run([action]);print(action_result.strip())
    report={'editor_sha256':hashlib.sha256(exe.read_bytes()).hexdigest(),'waypoint_actions':action_result.strip(),'editor_binary_selftest':(area/'editor-test-report.txt').read_text(encoding='utf-8').strip(),'c99_strict_builds_and_encoder_simulations':results,'task_adapter':task_result.strip(),'test_area':str(area),'installer':'separately validated','hardware_test':False}
    (ROOT/'build/validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print('Validation report:',ROOT/'build/validation.json')
if __name__=='__main__':main()
