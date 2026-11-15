from pathlib import Path
import hashlib,json,shutil,sys,zipfile
sys.stdout.reconfigure(encoding='utf-8')
ROOT=Path(__file__).resolve().parents[1];PROJECT=ROOT.parent
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 sys.path.insert(0,str(PROJECT/'tools'))
 from check_guides import main as check_guides
 check_guides()
 report=json.loads((ROOT/'build/acceptance.json').read_text(encoding='utf-8'))
 for field in ['custom_empty_repository','native_package_uninstall','clean_uninstall']:
  assert report.get(field)=='pass',field
 assert str(report.get('keil_headers','')).startswith('pass'),'keil_headers'
 src=ROOT/'dist/foxmolika_CubeMX_Setup_0.7.0_win-x64.exe';assert sha(src)==report['installer_sha256'],'tested binary differs from release'
 transactions=json.loads((ROOT/'build/transactions.json').read_text(encoding='utf-8'))
 assert transactions['passed'] and transactions['version']=='0.7.0'
 assert transactions['installer_sha256']==sha(src),'transaction tests used a different EXE'
 assert transactions['pack_sha256']==sha(PROJECT/'dist/RCS.foxmolika.0.7.0.pack'),'transaction tests used a different pack'
 actions=json.loads((PROJECT/'map_editor/build/validation.json').read_text(encoding='utf-8'))
 assert actions['editor_sha256']==sha(PROJECT/'map_editor/dist/foxmolika_MapEditor.exe'),'action tests used a different editor'
 assert actions['waypoint_actions'].startswith('PASS ') and '72 checks' in actions['editor_binary_selftest']
 style=json.loads((PROJECT/'build/style_validation.json').read_text(encoding='utf-8'))
 assert style['passed'] and style['editor_sha256']==actions['editor_sha256'],'style/migration tests used a different editor'
 assert (PROJECT/'docs/使用说明.html').read_bytes()==(ROOT/'docs/使用说明.html').read_bytes(),'tutorial drift'
 assert report['installed_editor_version']=='0.3.0' and '72 checks' in report['installed_editor_report']
 integration=json.loads((PROJECT/'build/project_integration_validation.json').read_text(encoding='utf-8'))
 assert integration['installer_sha256']==sha(src) and integration['keil']=='pass: ARM Compiler 6.22, 0 errors, 0 warnings'
 assert integration['four_headers_automatic']=='pass' and integration['delete_then_regenerate']=='pass' and integration['main_unchanged']
 assert integration['route_regenerate']=='pass' and integration['route_user_code_unchanged'] and integration['route_sources_compiled']=='pass: route/nav/actions'
 route_checks=json.loads((PROJECT/'build/route_project_validation.json').read_text(encoding='utf-8'))
 assert route_checks['passed'] and route_checks['editor_sha256']==actions['editor_sha256']
 assert route_checks['post_helper_sha256']==integration['post_helper_sha256']
 setup_checks=json.loads((PROJECT/'build/project_setup_validation.json').read_text(encoding='utf-8'));assert setup_checks['passed']
 assert setup_checks['project_setup_sha256']==integration['project_setup_sha256']
 dest=PROJECT/'dist'/src.name;shutil.copyfile(src,dest)
 shutil.copyfile(ROOT/'docs/使用说明.html',PROJECT/'dist/foxmolika_合并安装说明_0.7.0.html')
 shutil.copyfile(PROJECT/'map_editor/dist/使用说明.html',PROJECT/'dist/地图编辑器说明.html')
 shutil.copyfile(PROJECT/'docs/接入说明.md',PROJECT/'dist/底盘接入说明.md')
 shutil.copyfile(PROJECT/'docs/使用说明.html',PROJECT/'dist/使用说明.html')
 source=PROJECT/'dist/foxmolika_CubeMX_Source_0.7.0.zip'
 with zipfile.ZipFile(source,'w',zipfile.ZIP_DEFLATED) as z:
  folders=['firmware','templates','tools','docs','examples','installer','keil_headers','suite/src','suite/tools','suite/docs','map_editor/src','map_editor/templates','map_editor/firmware','map_editor/tools','map_editor/tests','map_editor/examples','map_editor/docs','tests']
  for folder in folders:
   for p in sorted((PROJECT/folder).rglob('*')):
    if p.is_file() and '__pycache__' not in p.parts:z.write(p,p.relative_to(PROJECT).as_posix())
  for name in ['.gitignore','.clang-format','LICENSE.txt','README.md','重新打包和验证.cmd','suite/README.md','suite/setup.manifest','map_editor/README.md','map_editor/installer.cs']:z.write(PROJECT/name,name)
  z.write(PROJECT/'native/native.ioc','native/native.ioc')
  z.write(ROOT/'build/acceptance.json','suite/validation/acceptance.json')
  z.write(ROOT/'build/transactions.json','suite/validation/transactions.json')
  z.write(PROJECT/'变更说明_0.7.0.md','变更说明_0.7.0.md')
  z.write(PROJECT/'map_editor/build/validation.json','suite/validation/waypoint-actions.json')
  z.write(PROJECT/'build/project_integration_validation.json','suite/validation/project-integration.json')
  z.write(PROJECT/'build/project_setup_validation.json','suite/validation/project-setup.json')
  z.write(PROJECT/'build/style_validation.json','suite/validation/style-generation.json')
  z.write(PROJECT/'build/style_audit.json','suite/validation/style-audit.json')
  z.write(PROJECT/'build/route_project_validation.json','suite/validation/route-project.json')
 release={'single_exe':str(dest),'sha256':sha(dest),'bytes':dest.stat().st_size,'test_report':report,'transaction_tests':transactions}
 (ROOT/'build/release.json').write_text(json.dumps(release,ensure_ascii=False,indent=2),encoding='utf-8')
 fixed=PROJECT/'dist/tester123_fixed_0.7.0.zip';assert fixed.is_file(),'package the validated fixed project first'
 (PROJECT/'dist/foxmolika_0.7.0_SHA256SUMS.txt').write_text(sha(dest)+'  '+dest.name+'\n'+sha(source)+'  '+source.name+'\n'+sha(fixed)+'  '+fixed.name+'\n',encoding='utf-8')
 sys.path.insert(0,str(PROJECT/'tools'))
 from build_pack import write_sums
 write_sums()
 (PROJECT/'dist/新安装请用合并版.txt').write_text('请使用 foxmolika_CubeMX_Setup_0.7.0_win-x64.exe。\n这是单文件合并安装包，包含底盘.pack自动注册及地图编辑器，替代之前分别部署的两个旧安装器。\n无需手动导入.pack；需要本机已安装完整CubeMX。详见 foxmolika_合并安装说明_0.7.0.html。\n',encoding='utf-8')
 print(dest);print('bytes',dest.stat().st_size);print('SHA256',sha(dest));print(source)
if __name__=='__main__':main()
