from pathlib import Path
import hashlib,json,shutil,sys,zipfile
sys.stdout.reconfigure(encoding='utf-8')
ROOT=Path(__file__).resolve().parents[1];PROJECT=ROOT.parent
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 report=json.loads((ROOT/'build/acceptance.json').read_text(encoding='utf-8'))
 for field in ['custom_empty_repository','native_package_uninstall','clean_uninstall']:
  assert report.get(field)=='pass',field
 assert str(report.get('keil_headers','')).startswith('pass'),'keil_headers'
 src=ROOT/'dist/RCS_CubeMX_Suite_Setup_0.3.3_win-x64.exe';assert sha(src)==report['installer_sha256'],'tested binary differs from release'
 transactions=json.loads((ROOT/'build/transactions.json').read_text(encoding='utf-8'))
 assert transactions['passed'] and transactions['version']=='0.3.3'
 assert transactions['installer_sha256']==sha(src),'transaction tests used a different EXE'
 assert transactions['pack_sha256']==sha(PROJECT/'dist/RCS.Chassis.0.3.3.pack'),'transaction tests used a different pack'
 actions=json.loads((PROJECT/'map_editor/build/validation.json').read_text(encoding='utf-8'))
 assert actions['editor_sha256']==sha(PROJECT/'map_editor/dist/RCS_MapEditor.exe'),'action tests used a different editor'
 assert actions['waypoint_actions'].startswith('PASS ') and '72 checks' in actions['editor_binary_selftest']
 assert report['installed_editor_version']=='0.2.0' and '72 checks' in report['installed_editor_report']
 dest=PROJECT/'dist'/src.name;shutil.copyfile(src,dest)
 shutil.copyfile(ROOT/'docs/使用说明.html',PROJECT/'dist/RCS_合并安装说明_0.3.3.html')
 shutil.copyfile(PROJECT/'map_editor/dist/使用说明.html',PROJECT/'dist/地图编辑器说明.html')
 shutil.copyfile(PROJECT/'docs/接入说明.md',PROJECT/'dist/底盘接入说明.md')
 shutil.copyfile(PROJECT/'docs/使用说明.html',PROJECT/'dist/使用说明.html')
 source=PROJECT/'dist/RCS_CubeMX_Suite_Source_0.3.3.zip'
 with zipfile.ZipFile(source,'w',zipfile.ZIP_DEFLATED) as z:
  folders=['firmware','tools','docs','examples','installer','keil_headers','suite/src','suite/tools','suite/docs','map_editor/src','map_editor/firmware','map_editor/tools','map_editor/tests','map_editor/examples','map_editor/docs','tests']
  for folder in folders:
   for p in sorted((PROJECT/folder).rglob('*')):
    if p.is_file() and '__pycache__' not in p.parts:z.write(p,p.relative_to(PROJECT).as_posix())
  for name in ['LICENSE.txt','README.md','重新打包和验证.cmd','suite/README.md','suite/setup.manifest','map_editor/README.md','map_editor/installer.cs']:z.write(PROJECT/name,name)
  z.write(PROJECT/'native/native.ioc','native/native.ioc')
  z.write(ROOT/'build/acceptance.json','suite/validation/acceptance.json')
  z.write(ROOT/'build/transactions.json','suite/validation/transactions.json')
  z.write(PROJECT/'变更说明_0.3.3.md','变更说明_0.3.3.md')
  z.write(PROJECT/'map_editor/build/validation.json','suite/validation/waypoint-actions.json')
 release={'single_exe':str(dest),'sha256':sha(dest),'bytes':dest.stat().st_size,'test_report':report,'transaction_tests':transactions}
 (ROOT/'build/release.json').write_text(json.dumps(release,ensure_ascii=False,indent=2),encoding='utf-8')
 (PROJECT/'dist/RCS_Suite_0.3.3_SHA256SUMS.txt').write_text(sha(dest)+'  '+dest.name+'\n'+sha(source)+'  '+source.name+'\n',encoding='utf-8')
 sys.path.insert(0,str(PROJECT/'tools'))
 from build_pack import write_sums
 write_sums()
 (PROJECT/'dist/新安装请用合并版.txt').write_text('请使用 RCS_CubeMX_Suite_Setup_0.3.3_win-x64.exe。\n这是单文件合并安装包，包含底盘.pack自动注册及地图编辑器，替代之前分别部署的两个旧安装器。\n无需手动导入.pack；需要本机已安装完整CubeMX。详见 RCS_合并安装说明_0.3.3.html。\n',encoding='utf-8')
 print(dest);print('bytes',dest.stat().st_size);print('SHA256',sha(dest));print(source)
if __name__=='__main__':main()
