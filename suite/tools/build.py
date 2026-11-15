from pathlib import Path
from xml.etree import ElementTree as E
import base64,hashlib,os,subprocess,sys,zipfile,shutil,re
sys.stdout.reconfigure(encoding='utf-8')
ROOT=Path(__file__).resolve().parents[1];PROJECT=ROOT.parent
sys.path.insert(0,str(PROJECT/'tools'))
from version import VERSION
def sha(b):return hashlib.sha256(b).hexdigest()
def run(args):
 p=subprocess.run([str(x) for x in args],cwd=ROOT,capture_output=True,text=True,errors='replace')
 if p.returncode:raise RuntimeError(p.stdout+'\n'+p.stderr)
 return p.stdout
def main():
 build=ROOT/'build';dist=ROOT/'dist';build.mkdir(exist_ok=True);dist.mkdir(exist_ok=True)
 assert (ROOT/'docs/使用说明.html').read_bytes()==(PROJECT/'docs/使用说明.html').read_bytes(),'Suite and pack tutorials must stay synchronized'
 src=(ROOT/'src/PackService.cs').read_text(encoding='utf-8')
 assert 'Version="'+VERSION+'"' in src,'PackService.Version must equal tools/version.py'
 for p in [ROOT/'src/Setup.cs',PROJECT/'keil_headers/KeilHeaders.cs']:
  values=re.findall(r'Assembly(?:File)?Version\("([^"]+)"\)',p.read_text(encoding='utf-8'))
  assert values and all(v==VERSION+'.0' for v in values),'Assembly version drift: '+str(p)
 ident=E.parse(ROOT/'setup.manifest').find('{urn:schemas-microsoft-com:asm.v1}assemblyIdentity')
 assert ident is not None and ident.attrib['version']==VERSION+'.0','Setup manifest version drift'
 editor=PROJECT/'map_editor/dist/foxmolika_MapEditor.exe'
 if not editor.is_file():raise RuntimeError('Build map_editor first')
 # Single source of truth: embed the exact pack produced by tools/build_pack.py,
 # so dist/RCS.foxmolika.<ver>.pack and the installer payload are byte-identical.
 pack=PROJECT/'dist'/('RCS.foxmolika.'+VERSION+'.pack')
 if not pack.is_file():raise RuntimeError('Run python tools/build_pack.py first; missing '+str(pack))
 with zipfile.ZipFile(pack) as z:packdata={n:z.read(n) for n in z.namelist()}
 cube=Path(os.environ.get('STM32CubeMX_PATH',str(Path.home()/'AppData/Local/Programs/STM32CubeMX')))
 run([cube/'jre/bin/javac.exe','--release','8','-d',build/'java',PROJECT/'tools/WorkspacePreferencesFactory.java'])
 csc=Path(os.environ.get('WINDIR','C:/Windows'))/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 keil=build/'foxmolika_KeilHeaders.exe'
 run([csc,'/nologo','/target:exe','/platform:anycpu','/optimize+','/reference:System.Xml.Linq.dll','/reference:System.Xml.dll','/reference:System.Core.dll','/out:'+str(keil),PROJECT/'keil_headers/KeilHeaders.cs',PROJECT/'keil_headers/ProjectBinding.cs'])
 post=build/'foxmolika_PostGenerate.exe'
 from build_integration import post_helper
 shutil.copyfile(post_helper(),post)
 project_setup=build/'foxmolika_ProjectSetup.exe'
 run([csc,'/nologo','/target:winexe','/platform:anycpu','/optimize+','/reference:System.Xml.Linq.dll','/reference:System.Xml.dll','/reference:System.Windows.Forms.dll','/reference:System.Core.dll','/resource:'+str(post)+',payload/foxmolika_PostGenerate.exe','/out:'+str(project_setup),PROJECT/'keil_headers/ProjectSetup.cs',PROJECT/'keil_headers/ProjectBinding.cs'])
 payload={'foxmolika_ProjectSetup.exe':project_setup,'foxmolika_MapEditor.exe':editor,'RCS.foxmolika.'+VERSION+'.pack':pack,'foxmolika_KeilHeaders.exe':keil,'使用说明.html':ROOT/'docs/使用说明.html',
  '地图编辑器说明.html':PROJECT/'map_editor/dist/使用说明.html','底盘接入说明.md':PROJECT/'docs/接入说明.md','LICENSE.txt':PROJECT/'LICENSE.txt',
  'examples/示例路线.rcsmap.json':PROJECT/'map_editor/examples/示例路线.rcsmap.json','examples/robot_route_task.c':PROJECT/'map_editor/examples/robot_route_task.c','examples/robot_route_task.h':PROJECT/'map_editor/examples/robot_route_task.h',
  'examples/立体地图示例.rcsmap.json':PROJECT/'map_editor/examples/立体地图示例.rcsmap.json',
  'sdk/rcs_chassis.c':PROJECT/'firmware/rcs_chassis.c','sdk/rcs_chassis.h':PROJECT/'firmware/rcs_chassis.h',
  'validation/WorkspacePreferencesFactory.class':build/'java/WorkspacePreferencesFactory.class','validation/WorkspacePreferencesFactory$MemoryPreferences.class':build/'java/WorkspacePreferencesFactory$MemoryPreferences.class'}
 manifest=E.Element('Suite',Version=VERSION,PackSha256=sha(pack.read_bytes()));app=E.SubElement(manifest,'AppFiles');pf=E.SubElement(manifest,'PackFiles')
 for name,p in payload.items():E.SubElement(app,'File',Name=name,Sha256=sha(p.read_bytes()))
 for name,b in sorted(packdata.items()):E.SubElement(pf,'File',Name=name,Sha256=sha(b))
 E.indent(manifest);m=build/'manifest.xml';E.ElementTree(manifest).write(m,encoding='utf-8',xml_declaration=True);payload['manifest.xml']=m
 setup=dist/('foxmolika_CubeMX_Setup_'+VERSION+'_win-x64.exe')
 args=[csc,'/nologo','/target:winexe','/platform:anycpu','/optimize+','/win32manifest:'+str(ROOT/'setup.manifest'),'/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Xml.Linq.dll','/reference:System.Core.dll','/out:'+str(setup)]
 args+=['/resource:'+str(p)+',payload/'+name for name,p in payload.items()];args+=sorted((ROOT/'src').glob('*.cs'))
 log=run(args);(build/'compile.log').write_text(log or 'Build succeeded without warnings.\n',encoding='utf-8')
 from windows_integrity import medium
 medium(setup)
 shutil.copyfile(ROOT/'docs/使用说明.html',dist/'使用说明.html')
 (dist/'SHA256SUMS.txt').write_text(sha(setup.read_bytes())+'  '+setup.name+'\n',encoding='utf-8')
 print(setup);print('bytes:',setup.stat().st_size)
if __name__=='__main__':main()
