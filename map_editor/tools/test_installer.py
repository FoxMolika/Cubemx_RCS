from pathlib import Path
import hashlib,json,subprocess,sys,uuid
sys.stdout.reconfigure(encoding='utf-8')
ROOT=Path(__file__).resolve().parents[1]
def run(args,expected=0):
    p=subprocess.run([str(x) for x in args],cwd=ROOT,capture_output=True,timeout=30)
    if p.returncode!=expected:raise RuntimeError(f'exit {p.returncode}, expected {expected}: {args[0]}')
def main():
    setup=ROOT/'dist/foxmolika_MapEditor_Setup_0.3.0_win-x64.exe'
    area=ROOT/'build'/('安装验收 空间_'+uuid.uuid4().hex[:8]);area.mkdir();dest=area/'安装目录'
    run([setup,'/S','/D='+str(dest)]);run([setup,'/S','/D='+str(dest)])
    assert (dest/'rcs-map-installed.xml').is_file()
    # Repair: a deleted payload file is restored by re-running the installer.
    (dest/'examples/robot_route_task.c').unlink();run([setup,'/S','/D='+str(dest)]);assert (dest/'examples/robot_route_task.c').is_file()
    assert hashlib.sha256((dest/'foxmolika_MapEditor.exe').read_bytes()).digest()==hashlib.sha256((ROOT/'dist/foxmolika_MapEditor.exe').read_bytes()).digest()
    run([dest/'foxmolika_MapEditor.exe','--selftest',area/'已安装程序自测'])
    unknown=area/'用户工程';unknown.mkdir();sentinel=unknown/'main.c';sentinel.write_bytes(b'main\r\n\x00\xff')
    before=sentinel.read_bytes();run([setup,'/S','/D='+str(unknown)],expected=2);assert sentinel.read_bytes()==before
    guide=dest/'使用说明.html';original=guide.read_bytes();guide.write_bytes(original+b'\nuser edit')
    run([setup,'/S','/UNINSTALL','/D='+str(dest)],expected=2);assert (dest/'foxmolika_MapEditor.exe').is_file();assert guide.read_bytes()!=original
    guide.write_bytes(original);extra=dest/'我的地图.txt';extra.write_bytes(b'user file preserved')
    run([setup,'/S','/UNINSTALL','/D='+str(dest)]);assert extra.read_bytes()==b'user file preserved';assert not (dest/'foxmolika_MapEditor.exe').exists()
    clean=area/'干净卸载';run([setup,'/S','/D='+str(clean)]);run([setup,'/S','/UNINSTALL','/D='+str(clean)]);assert not clean.exists()
    report={'actual_binary_install':'pass','repeat_install':'pass','repair_missing_file':'pass','chinese_spaced_paths':'pass','installed_editor_selftest':'pass','refuse_unknown_directory':'pass, main.c byte-identical','uninstall_modified_payload':'refused and preserved','uninstall_user_files':'preserved','clean_uninstall':'pass','test_directory':str(area)}
    (ROOT/'build/installer-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(report,ensure_ascii=False,indent=2))
if __name__=='__main__':main()
