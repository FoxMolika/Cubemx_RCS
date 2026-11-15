"""Exercise actual map/helper EXEs against owned MDK fixtures, including write failure."""
from pathlib import Path
from xml.etree import ElementTree as E
import base64,ctypes,hashlib,json,shutil,subprocess,sys,uuid,re,zipfile
from settings import DEFAULTS,config_header
R=Path(__file__).resolve().parents[1]
FILES=['rcs_route.c','rcs_route.h','rcs_nav.c','rcs_nav.h','rcs_actions.c','rcs_actions.h']
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 sys.stdout.reconfigure(encoding='utf-8');area=R/'build'/('routes_'+uuid.uuid4().hex[:8]);area.mkdir();exe=R/'map_editor/dist/foxmolika_MapEditor.exe';checks=[]
 def run(args,expected=0):
  p=subprocess.run([str(x) for x in args],capture_output=True,timeout=30)
  assert p.returncode==expected,(args,p.returncode,p.stdout,p.stderr,(exe.parent/'map-editor-error.log').read_text(encoding='utf-8-sig') if (exe.parent/'map-editor-error.log').exists() else '')
  return p
 def fixture(name,uv=True):
  root=area/name;root.mkdir();ioc=root/'tester123.ioc';ioc.write_bytes((R/'tests/fixtures/tester123_no_can.ioc').read_bytes())
  (root/'Core/Inc').mkdir(parents=True);(root/'Modules/Chassis').mkdir(parents=True);(root/'User/Inc').mkdir(parents=True);(root/'Maps').mkdir()
  for p in (R/'firmware').glob('*'):
   if p.suffix in ['.c','.h']:shutil.copyfile(p,root/'Modules/Chassis'/p.name)
  (root/'Core/Inc/rcs_config.h').write_text(config_header(DEFAULTS),encoding='utf-8');(root/'main.c').write_bytes(b'USER MAIN UNCHANGED\r\n')
  project=root/'MDK-ARM/tester123.uvprojx'
  if uv:
   project.parent.mkdir();xml=E.Element('Project');targets=E.SubElement(xml,'Targets');target=E.SubElement(targets,'Target');E.SubElement(target,'TargetName').text='DriveDebug'
   targetoption=E.SubElement(target,'TargetOption');E.SubElement(E.SubElement(targetoption,'TargetCommonOption'),'BeforeMake');option=E.SubElement(E.SubElement(targetoption,'TargetArmAds'),'Cads');values=E.SubElement(option,'VariousControls');E.SubElement(values,'IncludePath').text='../User/Inc;../Core/Inc;../Modules/Chassis';E.SubElement(values,'Define').text='KEEP_USER_DEFINE'
   groups=E.SubElement(target,'Groups');group=E.SubElement(groups,'Group');E.SubElement(group,'GroupName').text='ExistingChassis';files=E.SubElement(group,'Files')
   for name in ['rcs_chassis.c','rcs_control.c','rcs_hal.c']:
    file=E.SubElement(files,'File');E.SubElement(file,'FileName').text=name;E.SubElement(file,'FileType').text='1';E.SubElement(file,'FilePath').text='../Modules/Chassis/'+name
   user=E.SubElement(groups,'Group');E.SubElement(user,'GroupName').text='MyApplication';E.SubElement(user,'Files')
   E.ElementTree(xml).write(project,encoding='utf-8',xml_declaration=True)
  mapfile=root/'Maps/route.rcsmap.json';run([exe,'--demo',mapfile]);doc=json.loads(mapfile.read_text(encoding='utf-8'));custom=dict(doc['Points'][0]['DepartureActions'][0]);custom.update(Id=uuid.uuid4().hex,Kind=0,Name='custom',Start='/* START_HOOK */',Condition='1',Done='/* done */',Cancel='/* cancel */');doc['Points'][0]['DepartureActions'].append(custom);mapfile.write_text(json.dumps(doc,ensure_ascii=False),encoding='utf-8')
  return root,ioc,project,mapfile
 def route(root):return root/'Application/foxmolikaRoute'
 def post(root):return root/'foxmolika_Tools_080/foxmolika_PostGenerate.exe'
 def hashes(root):return {p.name:sha(p) for p in route(root).iterdir() if p.is_file()}
 def registered(root,project):
  xml=E.parse(project);target=xml.find('.//Target');files=target.findall('.//File');names=[f.findtext('FileName') for f in files]
  for name in FILES+['rcs_chassis.h','rcs_control.h','rcs_hal.h','rcs_config.h']:assert names.count(name)==1,(name,names)
  for name in FILES:
   f=next(f for f in files if f.findtext('FileName')==name);assert f.findtext('FileType')==('1' if name.endswith('.c') else '5');assert (project.parent/f.findtext('FilePath')).resolve()==(route(root)/name).resolve()
  include=target.find('.//Cads/VariousControls/IncludePath').text;assert include.startswith('../User/Inc;../Core/Inc;../Modules/Chassis;');assert include.split(';').count('../Application/foxmolikaRoute')==1
  assert target.find('.//Define').text=='KEEP_USER_DEFINE';assert any(g.findtext('GroupName')=='MyApplication' for g in target.findall('.//Group'))
  assert (root/'main.c').read_bytes()==b'USER MAIN UNCHANGED\r\n'
 root,ioc,project,mapfile=fixture('normal');before=project.read_bytes();run([exe,'--bind-project',mapfile,ioc]);assert project.read_bytes()==before and not route(root).exists();assert not Path(json.loads(mapfile.read_text(encoding='utf-8'))['ProjectIoc']).is_absolute();checks.append('association shared with editor; relative map link; no empty route group')
 run([exe,'--export-linked',mapfile]);registered(root,project);checks.append('actual editor registers route 3C3H/types/include while retaining user settings and main')
 before=project.read_bytes();files=hashes(root);marker=(root/'.foxmolika-project.xml').read_bytes();backups=set((root/'Application').glob('.rcs-route-backup-*'));run([exe,'--export-linked',mapfile]);assert project.read_bytes()==before and hashes(root)==files and (root/'.foxmolika-project.xml').read_bytes()==marker and set((root/'Application').glob('.rcs-route-backup-*'))==backups;checks.append('repeat export is byte-idempotent for sources, project and binding without new backups')
 action=route(root)/'rcs_actions.c';action.write_text(action.read_text(encoding='utf-8').replace('/* START_HOOK */','/* 用户手写保留 */'),encoding='utf-8');run([exe,'--export-linked',mapfile]);assert '/* 用户手写保留 */' in action.read_text(encoding='utf-8');checks.append('in-project re-export preserves handwritten USER CODE')
 doc=json.loads(mapfile.read_text(encoding='utf-8'));doc['Points'][0]['DepartureActions'][-1]['Start']='/* GUI CONFLICT */';mapfile.write_text(json.dumps(doc,ensure_ascii=False),encoding='utf-8');before=project.read_bytes();files=hashes(root);run([exe,'--export-linked',mapfile],2);assert project.read_bytes()==before and hashes(root)==files;doc['Points'][0]['DepartureActions'][-1]['Start']='/* START_HOOK */';mapfile.write_text(json.dumps(doc,ensure_ascii=False),encoding='utf-8');checks.append('dual USER CODE edit conflicts preserve files and project')
 out=area/'independent';before=project.read_bytes();run([exe,'--export',mapfile,out]);assert project.read_bytes()==before and set(p.name for p in out.iterdir())==set(FILES);checks.append('independent export ignores saved link and never modifies project')
 saved=root/'Elsewhere';saved.mkdir();othermap=saved/'save-as.rcsmap.json';run([exe,'--save-as-map',mapfile,othermap]);value=json.loads(othermap.read_text(encoding='utf-8'))['ProjectIoc'];assert (othermap.parent/value).resolve()==ioc.resolve();checks.append('Save As recalculates map-relative project link')
 moved=area/'moved';shutil.copytree(root,moved);run([exe,'--export-linked',moved/'Maps/route.rcsmap.json']);registered(moved,moved/'MDK-ARM/tester123.uvprojx');checks.append('copied project/map tree keeps portable association and relative MDK paths')
 xml=E.parse(project)
 for parent in xml.iter():
  for child in list(parent):
   if child.tag=='Group' and child.findtext('GroupName')=='Application/foxmolikaRoute':parent.remove(child)
   elif child.tag=='File' and child.findtext('FileName') in ['rcs_chassis.h','rcs_control.h','rcs_hal.h','rcs_config.h']:parent.remove(child)
 include=xml.find('.//Cads/VariousControls/IncludePath');include.text=include.text.replace(';../Application/foxmolikaRoute','');xml.write(project,encoding='utf-8');run([post(root)]);registered(root,project);before=project.read_bytes();run([post(root)]);assert project.read_bytes()==before;checks.append('actual post-helper restores all deleted file/include registrations idempotently')
 missing=route(root)/'rcs_nav.h';data=missing.read_bytes();missing.unlink();before=project.read_bytes();run([post(root)],2);assert project.read_bytes()==before;missing.write_bytes(data);checks.append('partial route directory refuses project writes')
 pending,pioc,puv,pmap=fixture('pending',False);run([exe,'--export-project',pmap,pioc]);assert route(pending).is_dir() and not puv.exists();checks.append('export before first Generate keeps files pending without inventing a Keil project')
 bad,bioc,buv,bmap=fixture('conflict');xml=E.parse(buv);files=xml.find('.//Files');f=E.SubElement(files,'File');E.SubElement(f,'FileName').text='rcs_nav.c';E.SubElement(f,'FilePath').text='../User/rcs_nav.c';xml.write(buv,encoding='utf-8');before=buv.read_bytes();original=bioc.read_bytes();run([exe,'--export-project',bmap,bioc],2);assert buv.read_bytes()==before and bioc.read_bytes()==original and not route(bad).exists() and not (bad/'.foxmolika-project.xml').exists();checks.append('another-directory same-name module refuses before association/export mutation')
 bad,bioc,buv,bmap=fixture('bad_xml');buv.write_text('<broken',encoding='utf-8');original=bioc.read_bytes();run([exe,'--export-project',bmap,bioc],2);assert bioc.read_bytes()==original and buv.read_text(encoding='utf-8')=='<broken' and not route(bad).exists();checks.append('malformed Keil XML is rejected before writes')
 bad,bioc,buv,bmap=fixture('binding');(bad/'.foxmolika-project.xml').write_text('<FoxmolikaProject Schema="1" Ioc="other.ioc" ProjectName="tester123" RouteDirectory="Application/foxmolikaRoute"/>',encoding='utf-8');original=bioc.read_bytes();run([exe,'--export-project',bmap,bioc],2);assert bioc.read_bytes()==original and not route(bad).exists();checks.append('binding to another IOC is retained and refused')
 bad,bioc,buv,bmap=fixture('escape');(bad/'.foxmolika-project.xml').write_text('<FoxmolikaProject Schema="1" Ioc="tester123.ioc" ProjectName="tester123" RouteDirectory="../outside"/>',encoding='utf-8');before=buv.read_bytes();run([exe,'--export-project',bmap,bioc],2);assert buv.read_bytes()==before and not route(bad).exists();checks.append('tampered route path escaping project is rejected')
 bad,bioc,buv,bmap=fixture('foreign_files');route(bad).mkdir(parents=True);(route(bad)/'my_code.c').write_bytes(b'keep my code');original=bioc.read_bytes();before=buv.read_bytes();run([exe,'--export-project',bmap,bioc],2);assert bioc.read_bytes()==original and buv.read_bytes()==before and not (bad/'.foxmolika-project.xml').exists() and (route(bad)/'my_code.c').read_bytes()==b'keep my code';checks.append('foreign output directory refuses before association writes')
 multi,mioc,muv,mmap=fixture('multi');xml=E.parse(muv);targets=xml.find('./Targets');other=E.fromstring(E.tostring(targets[0]));other.find('TargetName').text='DriveRelease';targets.append(other);support=E.SubElement(targets,'Target');E.SubElement(support,'TargetName').text='SupportOnly';E.SubElement(support,'Groups');support_before=E.tostring(support);xml.write(muv,encoding='utf-8');run([exe,'--export-project',mmap,mioc]);xml=E.parse(muv)
 for target in xml.findall('.//Target'):
  if target.findtext('TargetName')=='SupportOnly':assert not target.findall('.//File') and target.find('.//IncludePath') is None
  else:
   names=[f.findtext('FileName') for f in target.findall('.//File')];assert all(names.count(n)==1 for n in FILES)
 checks.append('multiple chassis targets receive routes; unrelated target remains untouched')
 linked,jioc,juv,jmap=fixture('junction');outside=area/'outside';outside.mkdir();(outside/'sentinel.txt').write_bytes(b'keep outside');q=subprocess.run(['cmd','/c','mklink','/J',str(linked/'Application'),str(outside)],capture_output=True);assert q.returncode==0,q.stderr;original=jioc.read_bytes();before=juv.read_bytes();run([exe,'--export-project',jmap,jioc],2);assert jioc.read_bytes()==original and juv.read_bytes()==before and list(p.name for p in outside.iterdir())==['sentinel.txt'];checks.append('junction route parent is refused without writing outside project')
 legacy,lioc,luv,lmap=fixture('legacy');content=lioc.read_bytes().replace(b'0.3.3',b'0.3.6').replace(b'ProjectManager.UAScriptAfterPath=',b'ProjectManager.UAScriptAfterPath=foxmolika_Tools_036/foxmolika_PostGenerate.exe');lioc.write_bytes(content);old=legacy/'foxmolika_Tools_036/foxmolika_PostGenerate.exe';old.parent.mkdir();old.write_bytes(base64.b64decode((R/'tests/fixtures/legacy_post_036.base64.txt').read_text()));oldbytes=old.read_bytes();run([exe,'--associate-project',lioc]);assert old.read_bytes()==oldbytes and post(legacy).is_file() and (Path(str(lioc)+'.rcs-before-080')).read_bytes()==content;checks.append('official 036 SHA whitelist upgrades hook while retaining old helper bytes and IOC backup')
 bad,bioc,buv,bmap=fixture('legacy_edit');bioc.write_bytes(content);old=bad/'foxmolika_Tools_036/foxmolika_PostGenerate.exe';old.parent.mkdir();old.write_bytes(oldbytes+b'user edit');run([exe,'--associate-project',bioc],2);assert bioc.read_bytes()==content and old.read_bytes()==oldbytes+b'user edit' and not post(bad).exists();checks.append('modified legacy hook is refused without overwriting it')
 locked,lioc,luv,lmap=fixture('lock');kernel=ctypes.WinDLL('kernel32',use_last_error=True);kernel.CreateFileW.argtypes=[ctypes.c_wchar_p,ctypes.c_uint32,ctypes.c_uint32,ctypes.c_void_p,ctypes.c_uint32,ctypes.c_uint32,ctypes.c_void_p];kernel.CreateFileW.restype=ctypes.c_void_p;kernel.CloseHandle.argtypes=[ctypes.c_void_p];handle=kernel.CreateFileW(str(luv),0x80000000,1,None,3,0,None);assert handle not in [None,ctypes.c_void_p(-1).value]
 before=luv.read_bytes()
 try:
  result=run([exe,'--export-project',lmap,lioc],2)
  assert luv.read_bytes()==before and set(p.name for p in route(locked).iterdir())==set(FILES)
  report=re.search(rb'([A-Za-z]:\\[^\r\n]*\.zip)',result.stderr)
  error=result.stderr.decode('utf-8',errors='replace')+' '+result.stderr.decode('gb18030',errors='replace')
  assert '路径文件已导出' in error,error
  if report:
   with zipfile.ZipFile(report.group(1).decode('utf-8')) as z:assert '路径文件已导出' in z.read('diagnostic.txt').decode('utf-8')
 finally:kernel.CloseHandle(handle)
 run([post(locked)]);registered(locked,luv);checks.append('locked UV write reports exported-but-pending; post-helper recovers after unlocking')
 result={'passed':True,'editor_sha256':sha(exe),'post_helper_sha256':sha(R/'build/foxmolika_PostGenerate.exe'),'checks':checks,'area':str(area),'physical_hardware':False};(R/'build/route_project_validation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');print('PASS',len(checks),'route/project integration scenarios (actual EXEs)')
if __name__=='__main__':main()
