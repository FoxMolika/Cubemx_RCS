"""Package ONLY validated current artifacts; never rebuild or install here."""
from pathlib import Path
from xml.etree import ElementTree as E
import hashlib,json,re,shutil,subprocess,sys,zipfile
R=Path(__file__).resolve().parents[1]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def read(name):return json.loads((R/name).read_text(encoding='utf-8'))
def main():
 sys.stdout.reconfigure(encoding='utf-8')
 from check_guides import main as guides
 guides()
 setup=R/'suite/dist/foxmolika_CubeMX_Setup_0.6.1_win-x64.exe'
 from windows_integrity import medium,security
 assert security(setup)[0]==8192
 permissions=read('build/permissions_validation.json');assert permissions['passed'] and permissions['installer_sha256']==sha(setup)
 continuous=read('build/continuous_validation.json');assert continuous['passed'] and continuous['editor_sha256']==sha(R/'map_editor/dist/foxmolika_MapEditor.exe')
 workspace=read('build/workspace_validation.json');assert workspace['passed'] and workspace['checks']>=41
 assert permissions['actual_install_token']['IntegrityRid']=='8192' and permissions['actual_install_token']['Elevated']=='false' and permissions['actual_install_token']['Administrator']=='false' and permissions['actual_install_token']['AppContainer']=='false'
 editor=R/'map_editor/dist/foxmolika_MapEditor.exe';pack=R/'dist/RCS.foxmolika.0.6.1.pack'
 assert workspace['editor_sha256']==sha(editor) and workspace['unfinished_draft_round_trip'] and workspace['inserted_transition_and_links_pass_geometry']
 accepted=read('suite/build/acceptance.json');integration=read('build/project_integration_validation.json')
 assert accepted.get('custom_empty_repository')=='pass' and accepted.get('native_package_uninstall')=='pass'
 assert accepted.get('clean_uninstall')=='pass' and accepted['installed_editor_version']=='0.6.1'
 assert accepted['installer_sha256']==sha(setup)
 editor_checks=int(re.search(r'PASS (\d+)',accepted['installed_editor_report']).group(1));assert editor_checks>=49
 assert int(re.search(r'PASS (\d+)',accepted['installed_workspace_report']).group(1))==workspace['checks'] and permissions['installed_workspace_checks']==workspace['checks']
 assert accepted['installed_continuous_report']==permissions['installed_continuous_report']==continuous['report']
 assert integration['installer_sha256']==sha(setup) and integration['installed_editor_sha256']==sha(editor)
 assert integration['keil']=='pass: ARM Compiler 6.22, 0 errors, 0 warnings'
 assert integration['route_sources_compiled']=='pass: route/nav/actions'
 assert integration['route_user_code_unchanged'] and integration['main_unchanged']
 phases=read('map_editor/build/phased_validation.json')
 assert len(phases['actual_export_simulations'])==3
 assert all('4205 assertions' in s for s in phases['actual_export_simulations'])
 phase_count=int(re.search(r'PASS (\d+) phase assertions',phases['unit_fault_phase_checks']).group(1))
 assert phase_count>=11448
 generated=Path(phases['area'])/'layout0/rcs_nav.c'
 assert generated.read_text(encoding='utf-8').split('/* RCS GENERATED SHA256')[0].strip()==(R/'map_editor/firmware/rcs_nav.c').read_text(encoding='utf-8').strip()
 crash=read('build/crash-validation/results.json');assert crash['passed'] and crash['checks']==54
 assert crash['editor_SHA256']==sha(editor)
 migration=read('build/migration061.json');assert migration['passed'] and migration['legacy_map_unchanged']
 course=read('build/course_validation.json');assert course['passed'] and course['editor_sha256']==sha(editor)
 assert course['same_generated_six_C_H_as_040'] and course['signed_arc_pixel_oracle']
 terrain=read('build/terrain_validation.json');assert terrain['passed'] and terrain['editor_sha256']==sha(editor)
 assert terrain['checks']>=95 and terrain['actual_drag_checks']>=38 and terrain['nonplanar_export_refused_before_mutation'] and terrain['terrain_map_unchanged']
 assert int(re.search(r'PASS (\d+)',accepted['installed_terrain_report']).group(1))==terrain['checks']
 route=read('build/route_project_validation.json');assert route['passed'] and route['editor_sha256']==sha(editor)
 assert route['post_helper_sha256']==sha(R/'build/foxmolika_PostGenerate.exe')
 revision=read('build/revision_validation.json');assert revision['passed'] and revision['editor_sha256']==sha(editor)
 keil=read('build/keil_guard_validation.json');assert keil['passed'] and keil['installer_sha256']==sha(setup)
 transactions=read('suite/build/transactions.json');assert transactions['passed']
 assert transactions['installer_sha256']==sha(setup) and transactions['pack_sha256']==sha(pack)
 core=(R/'build/host_validation.txt').read_text(encoding='utf-8');assert 'PASS 1555 checks' in core
 snapshot=read('build/original_037_SHA256.json');old=R.parent/'foxmolika_CubeMX_0.3.7'
 for name,digest in snapshot.items():assert sha(old/name)==digest,('original changed',name)
 previous=read('build/original_040_SHA256.json');previous_root=R.parent/'foxmolika_CubeMX_0.4.0'
 for name,digest in previous.items():assert sha(previous_root/name)==digest,('040 changed',name)
 previous041=read('build/original_041_SHA256.json');root041=R.parent/'foxmolika_CubeMX_0.4.1'
 for name,digest in previous041.items():assert sha(root041/name)==digest,('041 changed',name)
 previous050=read('build/original_050_SHA256.json');root050=R.parent/'foxmolika_CubeMX_0.5.0'
 for name,digest in previous050.items():assert sha(root050/name)==digest,('050 changed',name)
 previous051=read('build/original_051_SHA256.json');root051=R.parent/'foxmolika_CubeMX_0.5.1'
 for name,digest in previous051.items():assert sha(root051/name)==digest,('051 changed',name)
 previous052=read('build/original_052_SHA256.json');root052=R.parent/'foxmolika_CubeMX_0.5.2'
 for name,digest in previous052.items():assert sha(root052/name)==digest,('052 changed',name)
 previous060=read('build/original_060_SHA256.json');root060=R.parent/'foxmolika_CubeMX_0.6.0'
 for name,digest in previous060.items():assert sha(root060/name)==digest,('060 changed',name)
 # Hash actual embedded resources, rather than relying on source assumptions.
 p=subprocess.run(['powershell','-NoProfile','-ExecutionPolicy','Bypass','-File',str(R/'tools/audit_payload.ps1')],capture_output=True,check=True)
 resources=json.loads(p.stdout.decode('utf-8-sig'))
 manifest=R/'suite/build/manifest.xml';assert resources['payload/manifest.xml']==sha(manifest)
 for f in E.parse(manifest).findall('./AppFiles/File'):assert resources['payload/'+f.attrib['Name']]==f.attrib['Sha256']
 assert resources['payload/foxmolika_MapEditor.exe']==sha(editor)
 assert resources['payload/RCS.foxmolika.0.6.1.pack']==sha(pack)
 with zipfile.ZipFile(pack) as z:
  assert z.read('Documentation/使用说明.html')==(R/'docs/使用说明.html').read_bytes()
  for source in (R/'firmware').glob('*'):
   if source.suffix in ['.c','.h']:assert z.read('Middlewares/Third_Party/RCS_foxmolika/'+source.name)==source.read_bytes()==(old/'firmware'/source.name).read_bytes()
 dist=R/'dist';shutil.copyfile(setup,dist/setup.name)
 medium(dist/setup.name)
 with zipfile.ZipFile(dist/'foxmolika_CubeMX_Installer_0.6.1.zip','w',zipfile.ZIP_DEFLATED) as z:
  z.write(setup,setup.name)
  z.writestr('使用提示.txt','请在普通用户目录解压后双击EXE，默认不需管理员权限。不要在被标记为Low完整性的目录直接执行；若出现Low级别提示，请换到正常用户目录解压。安装器内嵌.pack并自动导入，不需要手动导入。')
 shutil.copyfile(R/'build/ui/continuous1.png',dist/'连续跨平台侧视图.png')
 shutil.copyfile(R/'build/ui/continuous2.png',dist/'连续跨平台三维图.png')
 shutil.copyfile(R/'map_editor/examples/连续通行示例.rcsmap.json',dist/'连续通行示例.rcsmap.json')
 shutil.copyfile(R/'build/ui/course_angles.png',dist/'夹角显示预览.png')
 for name,source in [('航点双动作框.png','phases.png'),('立体地图预览.png','terrain2.png'),('路线高度侧视图.png','terrain1.png')]:shutil.copyfile(R/'build/ui'/source,dist/name)
 shutil.copyfile(R/'build/ui/terrain0.png',dist/'地形拖动界面.png')
 shutil.copyfile(R/'build/ui/terrain0.png',dist/'主窗口一体编辑.png')
 shutil.copyfile(Path(workspace['area'])/'workspace-minimum.png',dist/'未完成草稿编辑.png')
 for name,source in [('使用说明.html','docs/使用说明.html'),('地图编辑器说明.html','map_editor/dist/使用说明.html'),('底盘接入说明.md','docs/接入说明.md')]:shutil.copyfile(R/source,dist/name)
 report={'version':'0.6.1','passed':True,'installer_sha256':sha(setup),'editor_sha256':sha(editor),'pack_sha256':sha(pack),
  'continuous_surface_checks':continuous['checks'],'original_060_files_unchanged':len(previous060),'features':['continuous connected-surface traversal without forced task waypoints','unified main-window waypoint and terrain workspace','exposed platform/ramp/connection/layer controls','shared document, global undo/redo and recovery snapshots','insert transition waypoint after selected point','draft save/open independent of full route validation','derived height and editable surface assignment','verified Medium non-admin installation','dual arrival/departure action panes','nonplanar firmware export blocked','Keil stale-map prebuild guard'],
  'tests':{'core_assertions':1555,'generated_layouts':3,'generated_assertions_per_layout':4205,'phase_assertions':phase_count,'editor_model_checks':editor_checks,'crash_checks':54,'route_integration_scenarios':len(route['checks']),'project_setup_scenarios':13,'revision_scenarios':len(revision['checks']),'installer_transaction_scenarios':8},
  'actual_CubeMX':'6.18.1; fresh isolated profiles, automatic import, alternate empty repository, native generate/regenerate, uninstall',
  'actual_Keil':'ARM Compiler 6.22; route/nav/actions compiled; 0 errors, 0 warnings',
  'actual_stale_map_blocks_Keil_build':True,'route_user_code_and_main_preserved':True,
  'original_037_files_unchanged':len(snapshot),'original_040_files_unchanged':len(previous),'original_041_files_unchanged':len(previous041),'original_050_files_unchanged':len(previous050),'original_051_files_unchanged':len(previous051),'terrain_checks':terrain['checks'],'actual_drag_checks':terrain['actual_drag_checks'],'embedded_resources_verified':len(resources),
  'ordinary_user_install':permissions,'published_installer_integrity_rid':security(dist/setup.name)[0],'original_052_files_unchanged':len(previous052),'workspace_checks':workspace['checks'],
  'hardware_test':False,'flashed':False,'second_physical_PC_test':False,'ui_review':'two-endpoint continuous route profile and 3D inspected; actual unified main window at 1440px and 1120px; top/profile/3D and shared point/property editors inspected; event-level main-window workflow tests passed',
  'delegated_review':'parallel DeepSeek design/edge-case reviews plus bounded topology review; Codex implemented, checked actual geometry/state/UI and ran installed/native regression',
  'limitations':['nonplanar routes are geometry-only: no slope firmware export or flat dynamic preview','axis-aligned rectangular surfaces only; no steps/lifts/automatic routing/IMU fusion','3D drawing shows upper surfaces; clearance includes configured underside thickness and envelope margin','encoder-only correction cannot observe all real slip','existing hardware driver support unchanged','custom-action preview is a placeholder','hard termination/power loss cannot guarantee immediate crash ZIP']}
 validation=R/'suite/validation/release-0.6.1.json';validation.parent.mkdir(exist_ok=True);validation.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
 out=dist/'foxmolika_CubeMX_Source_0.6.1.zip'
 excluded={'build','dist','__pycache__','.git','native','repaired_project'}
 with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED) as z:
  for f in sorted(R.rglob('*')):
   if not f.is_file():continue
   rel=f.relative_to(R)
   if any(p in excluded for p in rel.parts) or f.suffix.lower() in ['.exe','.dll','.log','.htm','.axf','.o','.crf','.hex','.pyc']:continue
   z.write(f,rel.as_posix())
 report['source_zip_sha256']=sha(out)
 (dist/'发布核验_0.6.1.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
 sums=[]
 for f in sorted(dist.iterdir()):
  if f.is_file() and f.suffix in ['.exe','.zip','.pack']:sums.append(sha(f)+'  '+f.name+'\n')
 (dist/'SHA256SUMS.txt').write_text(''.join(sums),encoding='ascii')
 print('PASS published verified single-file installer, source ZIP and synchronized tutorials')
 print('Installer SHA256',sha(setup));print('Installer bytes',setup.stat().st_size)
if __name__=='__main__':main()
